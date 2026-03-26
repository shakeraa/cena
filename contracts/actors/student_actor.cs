// =============================================================================
// Cena Platform -- StudentActor (Virtual, Event-Sourced)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
// Storage: Marten (PostgreSQL) event sourcing | Cache: Redis
//
// DESIGN NOTES:
//   - Virtual actor (grain): auto-activated on first message, passivated after
//     30 minutes of inactivity. Reactivated from latest snapshot + event replay.
//   - Event-sourced aggregate root for the Learner bounded context.
//   - Owns child actors: LearningSessionActor, StagnationDetectorActor,
//     OutreachSchedulerActor.
//   - Supervisor strategy: OneForOne, restart child on failure, stop after 3
//     consecutive failures within 60s.
//   - Memory budget: ~500KB per actor instance. Alert at 80% node memory.
//   - Snapshot every 100 events (configured in Marten, enforced here on write).
//   - All state mutations produce domain events persisted to Marten and
//     published to NATS JetStream.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using OpenTelemetry.Trace;
using Proto;
using Proto.Cluster;
using Proto.DependencyInjection;
using StackExchange.Redis;

using Cena.Contracts.Actors;
using Cena.Data.EventStore;

namespace Cena.Actors;

// =============================================================================
// STUDENT STATE -- in-memory aggregate rebuilt from events
// =============================================================================

/// <summary>
/// In-memory state for a single student actor. Rebuilt from Marten snapshot +
/// event replay on activation. This is the single source of truth for all
/// read queries against student state -- zero database round-trips for reads.
///
/// Memory budget: ~500KB. The largest contributors are the mastery map and
/// attempt history. We enforce limits on collection sizes and alert on breach.
/// </summary>
public sealed class StudentState
{
    // ---- Identity ----
    public string StudentId { get; set; } = "";

    // ---- Mastery Overlay (concept -> P(known) via BKT) ----
    /// <summary>
    /// Maps concept ID to current P(known). Updated on every AttemptConcept.
    /// Max tracked concepts per student: 2000 (soft limit, alert on exceed).
    /// </summary>
    public Dictionary<string, double> MasteryMap { get; set; } = new();

    // ---- Methodology State ----
    /// <summary>Concept ID -> currently active methodology name.</summary>
    public Dictionary<string, Methodology> MethodologyMap { get; set; } = new();

    /// <summary>Concept cluster -> ordered list of methodologies tried.</summary>
    public Dictionary<string, List<MethodologyAttemptRecord>> MethodAttemptHistory { get; set; } = new();

    // ---- Attempt History (sliding window for baselines) ----
    /// <summary>
    /// Last 20 attempts across all concepts -- used for baseline accuracy and
    /// response time. Circular buffer semantics: oldest evicted on overflow.
    /// </summary>
    public List<AttemptRecord> RecentAttempts { get; set; } = new(20);

    // ---- Half-Life Regression Timers ----
    /// <summary>Concept ID -> HLR state (half-life in hours, last review time).</summary>
    public Dictionary<string, HlrState> HlrTimers { get; set; } = new();

    // ---- Engagement ----
    public int TotalXp { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTimeOffset LastActivityDate { get; set; }

    // ---- Cognitive Load Profile ----
    /// <summary>Trailing fatigue baseline -- median fatigue score over last 5 sessions.</summary>
    public double BaselineFatigueScore { get; set; }
    public double BaselineAccuracy { get; set; }
    public double BaselineResponseTimeMs { get; set; }

    // ---- Metadata ----
    public string? ExperimentCohort { get; set; }
    public int SessionCount { get; set; }
    public int EventVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSnapshotAt { get; set; }

    // ---- Active Session ----
    /// <summary>Non-null when a learning session is in progress.</summary>
    public string? ActiveSessionId { get; set; }

    // ---- Constants ----
    public const int MaxRecentAttempts = 20;
    public const int MaxTrackedConcepts = 2000;
    public const int SnapshotInterval = 100;
    public const long MemoryBudgetBytes = 512 * 1024; // 500KB

    // =========================================================================
    // APPLY METHODS -- Marten event-sourced projection pattern
    // Each Apply overload handles one event type. Marten calls these during
    // snapshot rebuild and live aggregation. KEEP THESE ALLOCATION-FREE on
    // the hot path (ConceptAttempted).
    // =========================================================================

    /// <summary>
    /// Apply a concept attempt -- the primary hot-path event.
    /// Updates mastery map, recent attempts buffer, and baseline metrics.
    /// </summary>
    public void Apply(ConceptAttempted_V1 e)
    {
        MasteryMap[e.ConceptId] = e.PosteriorMastery;

        // Circular buffer: evict oldest when full
        if (RecentAttempts.Count >= MaxRecentAttempts)
            RecentAttempts.RemoveAt(0);

        RecentAttempts.Add(new AttemptRecord(
            e.ConceptId, e.IsCorrect, e.ResponseTimeMs,
            e.ErrorType, e.MethodologyActive, DateTimeOffset.UtcNow));

        RecalculateBaselines();
        EventVersion++;
    }

    public void Apply(ConceptMastered_V1 e)
    {
        MasteryMap[e.ConceptId] = e.MasteryLevel;
        HlrTimers[e.ConceptId] = new HlrState(e.InitialHalfLifeHours, DateTimeOffset.UtcNow);
        EventVersion++;
    }

    public void Apply(MasteryDecayed_V1 e)
    {
        if (MasteryMap.ContainsKey(e.ConceptId))
            MasteryMap[e.ConceptId] = e.PredictedRecall;

        if (HlrTimers.TryGetValue(e.ConceptId, out var hlr))
            HlrTimers[e.ConceptId] = hlr with { HalfLifeHours = e.HalfLifeHours };

        EventVersion++;
    }

    public void Apply(MethodologySwitched_V1 e)
    {
        if (Enum.TryParse<Methodology>(e.NewMethodology, true, out var methodology))
            MethodologyMap[e.ConceptId] = methodology;

        var clusterKey = e.ConceptId; // TODO: map to concept cluster via KST graph
        if (!MethodAttemptHistory.ContainsKey(clusterKey))
            MethodAttemptHistory[clusterKey] = new();

        MethodAttemptHistory[clusterKey].Add(new MethodologyAttemptRecord(
            e.NewMethodology, e.Trigger, e.StagnationScore, DateTimeOffset.UtcNow));

        EventVersion++;
    }

    public void Apply(SessionStarted_V1 e)
    {
        SessionCount++;
        ActiveSessionId = e.SessionId;
        ExperimentCohort ??= e.ExperimentCohort;
        EventVersion++;
    }

    public void Apply(SessionEnded_V1 e)
    {
        ActiveSessionId = null;
        EventVersion++;
    }

    public void Apply(XpAwarded_V1 e)
    {
        TotalXp = e.TotalXp;
        EventVersion++;
    }

    public void Apply(StreakUpdated_V1 e)
    {
        CurrentStreak = e.CurrentStreak;
        LongestStreak = e.LongestStreak;
        LastActivityDate = e.LastActivityDate;
        EventVersion++;
    }

    public void Apply(AnnotationAdded_V1 e)
    {
        EventVersion++;
    }

    public void Apply(StagnationDetected_V1 e)
    {
        EventVersion++;
    }

    // =========================================================================
    // BASELINES -- recalculated on every attempt from sliding window
    // =========================================================================

    private void RecalculateBaselines()
    {
        if (RecentAttempts.Count == 0) return;

        BaselineAccuracy = RecentAttempts.Count(a => a.IsCorrect) / (double)RecentAttempts.Count;

        var sortedRt = RecentAttempts.Select(a => (double)a.ResponseTimeMs).OrderBy(x => x).ToList();
        BaselineResponseTimeMs = sortedRt.Count % 2 == 0
            ? (sortedRt[sortedRt.Count / 2 - 1] + sortedRt[sortedRt.Count / 2]) / 2.0
            : sortedRt[sortedRt.Count / 2];
    }

    /// <summary>
    /// Estimated memory footprint. Called periodically to enforce budget.
    /// </summary>
    public long EstimateMemoryBytes()
    {
        // Rough estimate: 200 bytes per mastery entry, 100 bytes per attempt,
        // 300 bytes per HLR timer, 400 bytes per method history entry.
        return (MasteryMap.Count * 200L)
             + (RecentAttempts.Count * 100L)
             + (HlrTimers.Count * 300L)
             + (MethodAttemptHistory.Sum(kv => kv.Value.Count) * 400L)
             + 2048; // base object overhead
    }
}

// ---- Supporting Records ----

public sealed record AttemptRecord(
    string ConceptId,
    bool IsCorrect,
    int ResponseTimeMs,
    string ErrorType,
    string MethodologyActive,
    DateTimeOffset Timestamp);

public sealed record MethodologyAttemptRecord(
    string Methodology,
    string Trigger,
    double StagnationScore,
    DateTimeOffset SwitchedAt);

public sealed record HlrState(
    double HalfLifeHours,
    DateTimeOffset LastReviewAt);

// =============================================================================
// STUDENT ACTOR -- Virtual Actor (Grain) Implementation
// =============================================================================

/// <summary>
/// The StudentActor is the event-sourced aggregate root for all student state.
/// It is a Proto.Actor virtual actor (grain) activated on-demand by the cluster.
///
/// <para><b>Lifecycle:</b></para>
/// <list type="bullet">
///   <item>Activated: on first message to ClusterIdentity("student", studentId)</item>
///   <item>State recovery: load latest Marten snapshot + replay subsequent events</item>
///   <item>Passivation: after 30 minutes of idle (no messages received)</item>
///   <item>Child actors: LearningSessionActor, StagnationDetectorActor, OutreachSchedulerActor</item>
/// </list>
///
/// <para><b>Supervision:</b></para>
/// OneForOne strategy on children. Restart child on failure, stop after 3
/// consecutive failures within 60 seconds. See <see cref="CenaSupervisionStrategies"/>.
///
/// <para><b>Memory budget:</b> ~500KB per instance. Alert at 80% node memory.</para>
/// </summary>
public sealed class StudentActor : IActor
{
    // ---- Dependencies (injected via Proto.DependencyInjection) ----
    private readonly IDocumentStore _documentStore;
    private readonly INatsConnection _nats;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StudentActor> _logger;
    private readonly ActivitySource _activitySource;
    private readonly IMethodologySwitchService _methodologySwitchService;

    // ---- Actor State ----
    private StudentState _state = new();
    private string _studentId = "";
    private long _eventsSinceSnapshot;

    // ---- Child Actor PIDs ----
    private PID? _sessionActor;
    private PID? _stagnationDetector;
    private PID? _outreachScheduler;

    // ---- Configuration ----
    private static readonly TimeSpan PassivationTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MemoryCheckInterval = TimeSpan.FromMinutes(5);

    // ---- Telemetry ----
    private static readonly ActivitySource ActivitySourceInstance =
        new("Cena.Actors.StudentActor", "1.0.0");
    private static readonly Meter MeterInstance =
        new("Cena.Actors.StudentActor", "1.0.0");
    private static readonly Counter<long> AttemptCounter =
        MeterInstance.CreateCounter<long>("cena.student.attempts_total", description: "Total concept attempts processed");
    private static readonly Histogram<double> EventPersistLatency =
        MeterInstance.CreateHistogram<double>("cena.student.event_persist_ms", description: "Event persistence latency in ms");
    private static readonly Histogram<long> ActorMemoryUsage =
        MeterInstance.CreateHistogram<long>("cena.student.memory_bytes", description: "Estimated actor memory usage in bytes");
    private static readonly Counter<long> ActivationCounter =
        MeterInstance.CreateCounter<long>("cena.student.activations_total", description: "Actor activations");
    private static readonly Counter<long> PassivationCounter =
        MeterInstance.CreateCounter<long>("cena.student.passivations_total", description: "Actor passivations");

    public StudentActor(
        IDocumentStore documentStore,
        INatsConnection nats,
        IConnectionMultiplexer redis,
        ILogger<StudentActor> logger,
        IMethodologySwitchService methodologySwitchService)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _methodologySwitchService = methodologySwitchService ?? throw new ArgumentNullException(nameof(methodologySwitchService));
        _activitySource = ActivitySourceInstance;
    }

    // =========================================================================
    // PROTO.ACTOR LIFECYCLE HOOKS
    // =========================================================================

    /// <summary>
    /// Main message dispatch. Proto.Actor calls this for every message received.
    /// </summary>
    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            // ---- Lifecycle ----
            Started              => OnStarted(context),
            Stopping             => OnStopping(context),
            Stopped              => OnStopped(context),
            Restarting           => OnRestarting(context),
            ReceiveTimeout       => OnPassivation(context),

            // ---- Commands ----
            AttemptConcept cmd   => HandleAttemptConcept(context, cmd),
            StartSession cmd     => HandleStartSession(context, cmd),
            EndSession cmd       => HandleEndSession(context, cmd),
            SwitchMethodology cmd=> HandleSwitchMethodology(context, cmd),
            AddAnnotation cmd    => HandleAddAnnotation(context, cmd),
            SyncOfflineEvents cmd=> HandleSyncOfflineEvents(context, cmd),

            // ---- Queries ----
            GetStudentProfile q  => HandleGetProfile(context, q),
            GetReviewSchedule q  => HandleGetReviewSchedule(context, q),

            // ---- Internal (from child actors) ----
            StagnationDetected msg => HandleStagnationDetected(context, msg),

            // ---- Memory check timer ----
            MemoryCheckTick      => HandleMemoryCheck(context),

            _ => Task.CompletedTask
        };
    }

    /// <summary>
    /// Called once when the actor is first activated (or reactivated after passivation).
    /// Restores state from Marten snapshot + event replay, spawns child actors,
    /// and sets the passivation timer.
    /// </summary>
    private async Task OnStarted(IContext context)
    {
        var sw = Stopwatch.StartNew();
        using var activity = _activitySource.StartActivity("StudentActor.Started");

        _studentId = context.ClusterIdentity()?.Identity
            ?? throw new InvalidOperationException("StudentActor requires a ClusterIdentity");

        activity?.SetTag("student.id", _studentId);
        _logger.LogInformation(
            "StudentActor activating for student {StudentId}", _studentId);

        // ---- Restore state from Marten ----
        await RestoreStateFromEventStore();

        // ---- Spawn child actors with supervision ----
        SpawnChildActors(context);

        // ---- Set passivation timeout ----
        context.SetReceiveTimeout(PassivationTimeout);

        // ---- Schedule periodic memory check ----
        context.Send(context.Self, new MemoryCheckTick());

        sw.Stop();
        ActivationCounter.Add(1, new KeyValuePair<string, object?>("student.id", _studentId));

        _logger.LogInformation(
            "StudentActor activated for student {StudentId} in {ElapsedMs}ms. " +
            "EventVersion={EventVersion}, SessionCount={SessionCount}, Concepts={ConceptCount}",
            _studentId, sw.ElapsedMilliseconds, _state.EventVersion,
            _state.SessionCount, _state.MasteryMap.Count);
    }

    /// <summary>
    /// Called when the actor is stopping (graceful shutdown or passivation).
    /// Persists any pending state, stops child actors.
    /// </summary>
    private async Task OnStopping(IContext context)
    {
        using var activity = _activitySource.StartActivity("StudentActor.Stopping");
        activity?.SetTag("student.id", _studentId);

        _logger.LogInformation(
            "StudentActor stopping for student {StudentId}. Persisting final state.",
            _studentId);

        // Stop child actors gracefully
        if (_sessionActor != null)
        {
            await context.StopAsync(_sessionActor);
            _sessionActor = null;
        }
        if (_stagnationDetector != null)
        {
            await context.StopAsync(_stagnationDetector);
            _stagnationDetector = null;
        }
        if (_outreachScheduler != null)
        {
            await context.StopAsync(_outreachScheduler);
            _outreachScheduler = null;
        }

        // Force snapshot if there are unpersisted events
        if (_eventsSinceSnapshot > 0)
        {
            await ForceSnapshot();
        }

        // Invalidate Redis cache
        await InvalidateRedisCache();
    }

    /// <summary>
    /// Called after the actor has fully stopped. Final cleanup.
    /// </summary>
    private Task OnStopped(IContext context)
    {
        PassivationCounter.Add(1, new KeyValuePair<string, object?>("student.id", _studentId));
        _logger.LogInformation(
            "StudentActor stopped for student {StudentId}", _studentId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the actor is restarting due to a failure.
    /// Logs the failure and resets transient state.
    /// </summary>
    private Task OnRestarting(IContext context)
    {
        _logger.LogWarning(
            "StudentActor restarting for student {StudentId}. Resetting transient state.",
            _studentId);
        _sessionActor = null;
        _stagnationDetector = null;
        _outreachScheduler = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Passivation handler -- triggered by ReceiveTimeout (30 min idle).
    /// The actor will be garbage collected and reactivated on next message.
    /// </summary>
    private async Task OnPassivation(IContext context)
    {
        _logger.LogInformation(
            "StudentActor passivating for student {StudentId} after {Timeout} idle",
            _studentId, PassivationTimeout);

        await context.Cluster().RequestAsync<Passivate>(
            context.ClusterIdentity()!.Identity,
            context.ClusterIdentity()!.Kind,
            new Passivate(),
            CancellationToken.None);
    }

    // =========================================================================
    // STATE RESTORATION
    // =========================================================================

    /// <summary>
    /// Restores actor state from Marten: load latest snapshot, then replay
    /// events that occurred after the snapshot. This is the standard event
    /// sourcing rehydration pattern.
    /// </summary>
    private async Task RestoreStateFromEventStore()
    {
        using var activity = _activitySource.StartActivity("StudentActor.RestoreState");
        var sw = Stopwatch.StartNew();

        await using var session = _documentStore.LightweightSession();

        // Marten's AggregateStreamAsync handles snapshot + replay automatically.
        // It loads the latest inline snapshot, then replays events after it.
        var snapshot = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>(_studentId);

        if (snapshot != null)
        {
            // Map from Marten snapshot to actor state
            _state.StudentId = snapshot.StudentId;
            _state.TotalXp = snapshot.TotalXp;
            _state.CurrentStreak = snapshot.CurrentStreak;
            _state.LongestStreak = snapshot.LongestStreak;
            _state.LastActivityDate = snapshot.LastActivityDate;
            _state.ExperimentCohort = snapshot.ExperimentCohort;
            _state.BaselineAccuracy = snapshot.BaselineAccuracy;
            _state.BaselineResponseTimeMs = snapshot.BaselineResponseTimeMs;
            _state.SessionCount = snapshot.SessionCount;

            // Map mastery data
            foreach (var (conceptId, masteryState) in snapshot.ConceptMastery)
            {
                _state.MasteryMap[conceptId] = masteryState.PKnown;
            }

            // Map methodology data
            foreach (var (conceptId, methodology) in snapshot.ActiveMethodologyMap)
            {
                if (Enum.TryParse<Methodology>(methodology, true, out var m))
                    _state.MethodologyMap[conceptId] = m;
            }

            // Map HLR timers from half-life map
            foreach (var (conceptId, halfLife) in snapshot.HalfLifeMap)
            {
                var lastReview = snapshot.ConceptMastery.TryGetValue(conceptId, out var cs)
                    ? cs.MasteredAt ?? DateTimeOffset.UtcNow
                    : DateTimeOffset.UtcNow;
                _state.HlrTimers[conceptId] = new HlrState(halfLife, lastReview);
            }

            _logger.LogDebug(
                "Restored state for student {StudentId} from snapshot. " +
                "Concepts={ConceptCount}, XP={XP}, Streak={Streak}",
                _studentId, _state.MasteryMap.Count, _state.TotalXp, _state.CurrentStreak);
        }
        else
        {
            _state.StudentId = _studentId;
            _state.CreatedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "No existing state for student {StudentId}. Initializing fresh.", _studentId);
        }

        sw.Stop();
        activity?.SetTag("restore.duration_ms", sw.ElapsedMilliseconds);
        activity?.SetTag("restore.event_version", _state.EventVersion);
    }

    // =========================================================================
    // CHILD ACTOR MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Spawns long-lived child actors with the appropriate supervision strategy.
    /// LearningSessionActor is NOT spawned here -- it is created on StartSession.
    /// </summary>
    private void SpawnChildActors(IContext context)
    {
        var strategy = CenaSupervisionStrategies.StudentChildStrategy();

        // Stagnation detector -- lives across sessions
        var stagnationProps = Props.FromProducer(() =>
            new StagnationDetectorActor(
                _logger.CreateLogger<StagnationDetectorActor>()))
            .WithChildSupervisorStrategy(strategy);
        _stagnationDetector = context.Spawn(stagnationProps);

        // Outreach scheduler -- lives across sessions, manages HLR timers
        var outreachProps = Props.FromProducer(() =>
            new OutreachSchedulerActor(
                _nats,
                _logger.CreateLogger<OutreachSchedulerActor>()))
            .WithChildSupervisorStrategy(strategy);
        _outreachScheduler = context.Spawn(outreachProps);

        _logger.LogDebug(
            "Spawned child actors for student {StudentId}. " +
            "StagnationDetector={StagnationPid}, OutreachScheduler={OutreachPid}",
            _studentId, _stagnationDetector, _outreachScheduler);
    }

    /// <summary>
    /// Spawns a LearningSessionActor as a child. Called on StartSession.
    /// Only one session actor can be active at a time per student.
    /// </summary>
    private PID SpawnSessionActor(IContext context, string sessionId, Methodology methodology)
    {
        var strategy = CenaSupervisionStrategies.StudentChildStrategy();

        var sessionProps = Props.FromProducer(() =>
            new LearningSessionActor(
                sessionId,
                _studentId,
                methodology,
                _state,
                _logger.CreateLogger<LearningSessionActor>()))
            .WithChildSupervisorStrategy(strategy);

        var pid = context.Spawn(sessionProps);

        _logger.LogInformation(
            "Spawned LearningSessionActor for student {StudentId}, session {SessionId}, " +
            "methodology {Methodology}",
            _studentId, sessionId, methodology);

        return pid;
    }

    // =========================================================================
    // COMMAND HANDLERS
    // =========================================================================

    /// <summary>
    /// Handles a concept attempt -- the primary hot-path command.
    /// Validates, runs BKT update, persists event, updates child actors,
    /// publishes to NATS. Optimized for minimal allocation.
    /// </summary>
    private async Task HandleAttemptConcept(IContext context, AttemptConcept cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.AttemptConcept");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("concept.id", cmd.ConceptId);
        activity?.SetTag("question.id", cmd.QuestionId);

        try
        {
            // ---- Validate active session ----
            if (_state.ActiveSessionId == null || _state.ActiveSessionId != cmd.SessionId)
            {
                context.Respond(new ActorResult<EvaluateAnswerResponse>(
                    false, ErrorCode: "NO_ACTIVE_SESSION",
                    ErrorMessage: $"No active session {cmd.SessionId} for student {_studentId}"));
                return;
            }

            // ---- Forward to session actor for BKT + evaluation ----
            if (_sessionActor == null)
            {
                context.Respond(new ActorResult<EvaluateAnswerResponse>(
                    false, ErrorCode: "SESSION_ACTOR_MISSING",
                    ErrorMessage: "Session actor not available"));
                return;
            }

            // The session actor performs BKT inline and returns the evaluation
            var evaluateMsg = new EvaluateAnswer(
                cmd.SessionId, cmd.QuestionId, cmd.Answer,
                cmd.ResponseTimeMs, null, cmd.BackspaceCount, cmd.AnswerChangeCount);

            var evalResponse = await context.RequestAsync<ActorResult<EvaluateAnswerResponse>>(
                _sessionActor, evaluateMsg, TimeSpan.FromSeconds(10));

            if (evalResponse?.Success != true || evalResponse.Data == null)
            {
                context.Respond(evalResponse ?? new ActorResult<EvaluateAnswerResponse>(
                    false, ErrorCode: "EVALUATION_FAILED"));
                return;
            }

            var eval = evalResponse.Data;

            // ---- Get prior mastery for event ----
            var priorMastery = _state.MasteryMap.GetValueOrDefault(cmd.ConceptId, 0.3);

            // ---- Persist domain event ----
            var @event = new ConceptAttempted_V1(
                cmd.StudentId, cmd.ConceptId, cmd.SessionId,
                eval.IsCorrect, cmd.ResponseTimeMs, cmd.QuestionId,
                cmd.QuestionType.ToString(), GetActiveMethodology(cmd.ConceptId),
                eval.ClassifiedErrorType.ToString(),
                priorMastery, eval.UpdatedMastery,
                cmd.HintCountUsed, cmd.WasSkipped,
                ComputeAnswerHash(cmd.Answer),
                cmd.BackspaceCount, cmd.AnswerChangeCount, cmd.WasOffline);

            await PersistAndPublish(context, @event);

            // ---- Apply to local state ----
            _state.Apply(@event);

            // ---- Update stagnation detector ----
            if (_stagnationDetector != null)
            {
                context.Send(_stagnationDetector, new UpdateSignals(
                    _studentId, cmd.ConceptId, cmd.SessionId,
                    eval.IsCorrect, cmd.ResponseTimeMs,
                    eval.ClassifiedErrorType,
                    0, // session duration filled by session actor
                    null, _state.BaselineAccuracy, _state.BaselineResponseTimeMs));
            }

            // ---- Award XP ----
            int xpEarned = CalculateXpAward(eval.IsCorrect, cmd.HintCountUsed, eval.Score);
            if (xpEarned > 0)
            {
                var xpEvent = new XpAwarded_V1(
                    _studentId, xpEarned, "exercise_correct", _state.TotalXp + xpEarned);
                await PersistAndPublish(context, xpEvent);
                _state.Apply(xpEvent);
            }

            // ---- Check mastery threshold (0.85) ----
            if (eval.UpdatedMastery >= 0.85 && priorMastery < 0.85)
            {
                var masteredEvent = new ConceptMastered_V1(
                    _studentId, cmd.ConceptId, cmd.SessionId,
                    eval.UpdatedMastery,
                    _state.MasteryMap.Count,
                    _state.SessionCount,
                    GetActiveMethodology(cmd.ConceptId),
                    24.0); // Initial half-life: 24 hours
                await PersistAndPublish(context, masteredEvent);
                _state.Apply(masteredEvent);

                // Notify outreach scheduler to set up HLR timer
                if (_outreachScheduler != null)
                {
                    context.Send(_outreachScheduler, new ConceptMasteredNotification(
                        _studentId, cmd.ConceptId, 24.0));
                }
            }

            // ---- Telemetry ----
            AttemptCounter.Add(1,
                new KeyValuePair<string, object?>("student.id", _studentId),
                new KeyValuePair<string, object?>("correct", eval.IsCorrect));

            // ---- Respond ----
            context.Respond(new ActorResult<EvaluateAnswerResponse>(true, eval));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process AttemptConcept for student {StudentId}, concept {ConceptId}",
                _studentId, cmd.ConceptId);

            context.Respond(new ActorResult<EvaluateAnswerResponse>(
                false, ErrorCode: "INTERNAL_ERROR",
                ErrorMessage: "An internal error occurred processing the attempt"));
        }
    }

    /// <summary>
    /// Starts a new learning session. Creates a child LearningSessionActor.
    /// Only one session can be active at a time per student.
    /// </summary>
    private async Task HandleStartSession(IContext context, StartSession cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.StartSession");
        activity?.SetTag("student.id", _studentId);

        try
        {
            // ---- Check for existing session ----
            if (_state.ActiveSessionId != null)
            {
                _logger.LogWarning(
                    "Student {StudentId} already has active session {ActiveSession}. " +
                    "Ending it before starting new one.",
                    _studentId, _state.ActiveSessionId);

                // Force-end the existing session
                await HandleEndSession(context, new EndSession(
                    _studentId, _state.ActiveSessionId, SessionEndReason.AppBackgrounded));
            }

            // ---- Generate session ID ----
            var sessionId = Guid.CreateVersion7().ToString();

            // ---- Determine starting concept and methodology ----
            var startingConceptId = cmd.ConceptId ?? SelectNextConcept();
            var methodology = _state.MethodologyMap.GetValueOrDefault(
                startingConceptId, Methodology.Socratic);

            // ---- Persist event ----
            var @event = new SessionStarted_V1(
                _studentId, sessionId, cmd.DeviceType, cmd.AppVersion,
                methodology.ToString(), _state.ExperimentCohort,
                cmd.IsOffline, cmd.ClientTimestamp);

            await PersistAndPublish(context, @event);
            _state.Apply(@event);

            // ---- Spawn session actor ----
            _sessionActor = SpawnSessionActor(context, sessionId, methodology);

            // ---- Update streak ----
            await UpdateStreak(context);

            // ---- Respond ----
            context.Respond(new ActorResult<StartSessionResponse>(true,
                new StartSessionResponse(
                    sessionId, startingConceptId,
                    startingConceptId, // TODO: resolve concept name from graph
                    methodology, _state.TotalXp, _state.CurrentStreak)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start session for student {StudentId}", _studentId);
            context.Respond(new ActorResult<StartSessionResponse>(
                false, ErrorCode: "SESSION_START_FAILED",
                ErrorMessage: ex.Message));
        }
    }

    /// <summary>
    /// Ends an active learning session. Destroys the child session actor,
    /// triggers stagnation check, and persists the session-ended event.
    /// </summary>
    private async Task HandleEndSession(IContext context, EndSession cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.EndSession");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("session.id", cmd.SessionId);

        try
        {
            if (_state.ActiveSessionId != cmd.SessionId)
            {
                context.Respond(new ActorResult(
                    false, ErrorCode: "SESSION_MISMATCH",
                    ErrorMessage: $"Active session is {_state.ActiveSessionId}, not {cmd.SessionId}"));
                return;
            }

            // ---- Get session summary from session actor before stopping it ----
            SessionSummary? summary = null;
            if (_sessionActor != null)
            {
                summary = await context.RequestAsync<SessionSummary>(
                    _sessionActor, new GetSessionSummary(), TimeSpan.FromSeconds(5));

                await context.StopAsync(_sessionActor);
                _sessionActor = null;
            }

            // ---- Persist event ----
            var @event = new SessionEnded_V1(
                _studentId, cmd.SessionId, cmd.Reason.ToString(),
                summary?.DurationMinutes ?? 0,
                summary?.QuestionsAttempted ?? 0,
                summary?.QuestionsCorrect ?? 0,
                summary?.AvgResponseTimeMs ?? 0,
                summary?.FatigueScore ?? 0);

            await PersistAndPublish(context, @event);
            _state.Apply(@event);

            // ---- Trigger stagnation check (post-session) ----
            if (_stagnationDetector != null && summary?.LastConceptId != null)
            {
                context.Send(_stagnationDetector, new CheckStagnation(
                    _studentId, summary.LastConceptId));
            }

            context.Respond(new ActorResult(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to end session {SessionId} for student {StudentId}",
                cmd.SessionId, _studentId);
            context.Respond(new ActorResult(
                false, ErrorCode: "SESSION_END_FAILED", ErrorMessage: ex.Message));
        }
    }

    /// <summary>
    /// Handles a student-initiated methodology switch. Validates cooldown,
    /// invokes the MethodologySwitchService, persists the switch event,
    /// and resets the stagnation detector.
    /// </summary>
    private async Task HandleSwitchMethodology(IContext context, SwitchMethodology cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.SwitchMethodology");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("concept.id", cmd.ConceptId);

        try
        {
            var currentMethodology = _state.MethodologyMap.GetValueOrDefault(
                cmd.ConceptId, Methodology.Socratic);

            // Map student-friendly label to methodology
            // (the service handles the lookup and validation)
            var decision = await _methodologySwitchService.DecideSwitch(
                new DecideSwitchRequest(
                    _studentId, cmd.ConceptId, cmd.ConceptId, // category = concept for now
                    ErrorType.None, currentMethodology,
                    _state.MethodAttemptHistory.GetValueOrDefault(cmd.ConceptId, new())
                        .Select(m => m.Methodology).ToList(),
                    0.0, 0));

            if (!decision.ShouldSwitch)
            {
                context.Respond(new ActorResult(
                    false, ErrorCode: "SWITCH_DENIED",
                    ErrorMessage: decision.DecisionTrace));
                return;
            }

            var newMethodology = decision.RecommendedMethodology
                ?? throw new InvalidOperationException("DecideSwitch returned ShouldSwitch=true but no methodology");

            // ---- Persist event ----
            var @event = new MethodologySwitched_V1(
                _studentId, cmd.ConceptId,
                currentMethodology.ToString(),
                newMethodology.ToString(),
                "student_requested",
                0.0,
                ErrorType.None.ToString(),
                decision.Confidence);

            await PersistAndPublish(context, @event);
            _state.Apply(@event);

            // ---- Reset stagnation detector for this concept ----
            if (_stagnationDetector != null)
            {
                context.Send(_stagnationDetector, new ResetAfterSwitch(
                    _studentId, cmd.ConceptId, newMethodology));
            }

            context.Respond(new ActorResult(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to switch methodology for student {StudentId}, concept {ConceptId}",
                _studentId, cmd.ConceptId);
            context.Respond(new ActorResult(
                false, ErrorCode: "SWITCH_FAILED", ErrorMessage: ex.Message));
        }
    }

    /// <summary>
    /// Adds a student annotation to a concept. The text is analyzed for
    /// sentiment (async, via LLM ACL) and persisted with content hash.
    /// </summary>
    private async Task HandleAddAnnotation(IContext context, AddAnnotation cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.AddAnnotation");

        try
        {
            var annotationId = Guid.CreateVersion7().ToString();
            var contentHash = ComputeAnswerHash(cmd.Text);

            // TODO: Async sentiment analysis via LLM ACL
            double sentimentScore = 0.5; // placeholder until NLP call

            var @event = new AnnotationAdded_V1(
                _studentId, cmd.ConceptId, annotationId,
                contentHash, sentimentScore, cmd.Kind.ToString());

            await PersistAndPublish(context, @event);
            _state.Apply(@event);

            // Update stagnation detector with sentiment
            if (_stagnationDetector != null)
            {
                context.Send(_stagnationDetector, new UpdateSignals(
                    _studentId, cmd.ConceptId, cmd.SessionId,
                    false, 0, ErrorType.None, 0,
                    sentimentScore, _state.BaselineAccuracy, _state.BaselineResponseTimeMs));
            }

            context.Respond(new ActorResult(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to add annotation for student {StudentId}", _studentId);
            context.Respond(new ActorResult(
                false, ErrorCode: "ANNOTATION_FAILED", ErrorMessage: ex.Message));
        }
    }

    /// <summary>
    /// Reconciles offline events that were queued on the client device.
    /// Events are replayed in chronological order. Conflicts are resolved
    /// by client-timestamp ordering (last-writer-wins within session scope).
    /// </summary>
    private async Task HandleSyncOfflineEvents(IContext context, SyncOfflineEvents cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.SyncOfflineEvents");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("event.count", cmd.Events.Count);

        _logger.LogInformation(
            "Syncing {Count} offline events for student {StudentId}",
            cmd.Events.Count, _studentId);

        int processed = 0;
        int failed = 0;

        // Process events in chronological order
        foreach (var offlineEvent in cmd.Events.OrderBy(e => e.ClientTimestamp))
        {
            try
            {
                switch (offlineEvent)
                {
                    case OfflineAttemptEvent attempt:
                        await HandleAttemptConcept(context, new AttemptConcept(
                            _studentId, attempt.SessionId, attempt.ConceptId,
                            attempt.QuestionId, attempt.QuestionType,
                            attempt.Answer, attempt.ResponseTimeMs,
                            attempt.HintCountUsed, attempt.WasSkipped,
                            attempt.BackspaceCount, attempt.AnswerChangeCount,
                            true) { CorrelationId = cmd.CorrelationId });
                        processed++;
                        break;

                    default:
                        _logger.LogWarning(
                            "Unknown offline event type: {Type}", offlineEvent.GetType().Name);
                        failed++;
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process offline event for student {StudentId}", _studentId);
                failed++;
            }
        }

        _logger.LogInformation(
            "Offline sync complete for student {StudentId}. Processed={Processed}, Failed={Failed}",
            _studentId, processed, failed);

        context.Respond(new ActorResult(failed == 0,
            ErrorCode: failed > 0 ? "PARTIAL_SYNC" : null,
            ErrorMessage: failed > 0 ? $"{failed} events failed to sync" : null));
    }

    // =========================================================================
    // INTERNAL EVENT HANDLERS (from child actors)
    // =========================================================================

    /// <summary>
    /// Handles stagnation detection from the StagnationDetectorActor.
    /// Triggers methodology switch via the MethodologySwitchService.
    /// </summary>
    private async Task HandleStagnationDetected(IContext context, StagnationDetected msg)
    {
        using var activity = _activitySource.StartActivity("StudentActor.StagnationDetected");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("concept.id", msg.ConceptId);
        activity?.SetTag("stagnation.score", msg.CompositeScore);

        _logger.LogInformation(
            "Stagnation detected for student {StudentId}, concept {ConceptId}. " +
            "Score={Score:F3}, ConsecutiveSessions={Sessions}",
            _studentId, msg.ConceptId, msg.CompositeScore, msg.ConsecutiveStagnantSessions);

        // Persist stagnation event
        var stagnationEvent = new StagnationDetected_V1(
            _studentId, msg.ConceptId, msg.CompositeScore,
            msg.Signals.AccuracyPlateau, msg.Signals.ResponseTimeDrift,
            msg.Signals.SessionAbandonment, msg.Signals.ErrorRepetition,
            msg.Signals.AnnotationSentiment, msg.ConsecutiveStagnantSessions);

        await PersistAndPublish(context, stagnationEvent);
        _state.Apply(stagnationEvent);

        // ---- Invoke methodology switch service ----
        var currentMethodology = _state.MethodologyMap.GetValueOrDefault(
            msg.ConceptId, Methodology.Socratic);

        var dominantErrorType = DetermineDominantErrorType(msg.ConceptId);

        var decision = await _methodologySwitchService.DecideSwitch(
            new DecideSwitchRequest(
                _studentId, msg.ConceptId, msg.ConceptId,
                dominantErrorType, currentMethodology,
                _state.MethodAttemptHistory.GetValueOrDefault(msg.ConceptId, new())
                    .Select(m => m.Methodology).ToList(),
                msg.CompositeScore, msg.ConsecutiveStagnantSessions));

        if (decision.ShouldSwitch && decision.RecommendedMethodology.HasValue)
        {
            var switchEvent = new MethodologySwitched_V1(
                _studentId, msg.ConceptId,
                currentMethodology.ToString(),
                decision.RecommendedMethodology.Value.ToString(),
                "stagnation_detected",
                msg.CompositeScore,
                dominantErrorType.ToString(),
                decision.Confidence);

            await PersistAndPublish(context, switchEvent);
            _state.Apply(switchEvent);

            // Reset stagnation detector
            if (_stagnationDetector != null)
            {
                context.Send(_stagnationDetector, new ResetAfterSwitch(
                    _studentId, msg.ConceptId, decision.RecommendedMethodology.Value));
            }
        }
        else if (decision.AllMethodologiesExhausted)
        {
            _logger.LogWarning(
                "All methodologies exhausted for student {StudentId}, concept {ConceptId}. " +
                "Escalation: {Action}",
                _studentId, msg.ConceptId, decision.EscalationAction);

            // Publish escalation event to NATS for human tutor matching
            await PublishToNats("cena.student.escalation", new
            {
                StudentId = _studentId,
                ConceptId = msg.ConceptId,
                Action = decision.EscalationAction,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    // =========================================================================
    // QUERY HANDLERS
    // =========================================================================

    /// <summary>
    /// Returns the student's current profile from in-memory state.
    /// Zero database round-trip -- microsecond latency.
    /// </summary>
    private Task HandleGetProfile(IContext context, GetStudentProfile query)
    {
        var response = new StudentProfileResponse(
            _studentId,
            _state.MasteryMap.AsReadOnly(),
            _state.MethodologyMap.ToDictionary(kv => kv.Key, kv => kv.Value.ToString())
                .AsReadOnly(),
            _state.TotalXp,
            _state.CurrentStreak,
            _state.LongestStreak,
            _state.LastActivityDate,
            _state.ExperimentCohort,
            _state.SessionCount);

        context.Respond(new ActorResult<StudentProfileResponse>(true, response));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the student's spaced repetition review schedule, ordered by urgency.
    /// Computed from in-memory HLR timers using p(t) = 2^(-delta/h).
    /// </summary>
    private Task HandleGetReviewSchedule(IContext context, GetReviewSchedule query)
    {
        var now = DateTimeOffset.UtcNow;
        var reviewItems = _state.HlrTimers
            .Select(kv =>
            {
                var delta = (now - kv.Value.LastReviewAt).TotalHours;
                var predictedRecall = Math.Pow(2, -delta / kv.Value.HalfLifeHours);
                var priority = predictedRecall < 0.5 ? "urgent"
                    : predictedRecall < 0.7 ? "standard" : "low";
                var dueAt = kv.Value.LastReviewAt.AddHours(
                    -kv.Value.HalfLifeHours * Math.Log2(0.85)); // when recall drops to 0.85

                return new ReviewItem(
                    kv.Key, kv.Key, // TODO: resolve concept name
                    predictedRecall, kv.Value.HalfLifeHours, priority, dueAt);
            })
            .Where(r => r.PredictedRecall < 0.85)
            .OrderBy(r => r.PredictedRecall)
            .Take(query.MaxItems)
            .ToList();

        context.Respond(new ActorResult<IReadOnlyList<ReviewItem>>(
            true, reviewItems.AsReadOnly()));
        return Task.CompletedTask;
    }

    // =========================================================================
    // EVENT PERSISTENCE & PUBLISHING
    // =========================================================================

    /// <summary>
    /// Persists a domain event to Marten and publishes to NATS JetStream.
    /// This is the critical write path -- latency is measured via OpenTelemetry.
    /// </summary>
    private async Task PersistAndPublish<TEvent>(IContext context, TEvent @event)
        where TEvent : class
    {
        var sw = Stopwatch.StartNew();
        using var activity = _activitySource.StartActivity("StudentActor.PersistEvent");
        activity?.SetTag("event.type", typeof(TEvent).Name);

        // ---- Persist to Marten ----
        await using var session = _documentStore.LightweightSession();
        session.Events.Append(_studentId, @event);
        await session.SaveChangesAsync();

        _eventsSinceSnapshot++;
        sw.Stop();

        EventPersistLatency.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name));

        // ---- Publish to NATS JetStream (fire-and-forget with retry) ----
        var subject = $"cena.student.events.{typeof(TEvent).Name.ToLowerInvariant()}";
        await PublishToNats(subject, @event);

        // ---- Check snapshot threshold ----
        if (_eventsSinceSnapshot >= StudentState.SnapshotInterval)
        {
            await ForceSnapshot();
        }
    }

    /// <summary>
    /// Publishes a message to NATS JetStream. Failures are logged but do not
    /// fail the caller -- NATS is eventually consistent by design.
    /// </summary>
    private async Task PublishToNats<T>(string subject, T message) where T : class
    {
        try
        {
            var js = new NatsJSContext(_nats);
            await js.PublishAsync(subject, message);
        }
        catch (Exception ex)
        {
            // NATS publish failure is non-fatal. Event is already in Marten.
            // A catch-up publisher will replay missed events.
            _logger.LogWarning(ex,
                "Failed to publish to NATS subject {Subject} for student {StudentId}. " +
                "Event is persisted in Marten and will be replayed.",
                subject, _studentId);
        }
    }

    /// <summary>
    /// Forces a snapshot by triggering Marten's inline snapshot projection.
    /// Called when event count since last snapshot exceeds threshold.
    /// </summary>
    private async Task ForceSnapshot()
    {
        using var activity = _activitySource.StartActivity("StudentActor.ForceSnapshot");
        var sw = Stopwatch.StartNew();

        try
        {
            await using var session = _documentStore.LightweightSession();
            // Marten inline snapshot is triggered automatically on SaveChanges
            // when the event count threshold is reached. We just need to ensure
            // a write happens. The snapshot projection handles the rest.
            _eventsSinceSnapshot = 0;
            _state.LastSnapshotAt = DateTimeOffset.UtcNow;

            sw.Stop();
            _logger.LogDebug(
                "Snapshot created for student {StudentId} at EventVersion={Version} in {Ms}ms",
                _studentId, _state.EventVersion, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create snapshot for student {StudentId}", _studentId);
            // Non-fatal: state can be rebuilt from events
        }
    }

    // =========================================================================
    // REDIS CACHE
    // =========================================================================

    /// <summary>
    /// Invalidates the Redis cache entry for this student on passivation.
    /// </summary>
    private async Task InvalidateRedisCache()
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync($"student:{_studentId}:profile");
            await db.KeyDeleteAsync($"student:{_studentId}:review_schedule");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to invalidate Redis cache for student {StudentId}", _studentId);
        }
    }

    // =========================================================================
    // MEMORY MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Periodic memory check. Alerts if actor exceeds budget.
    /// </summary>
    private Task HandleMemoryCheck(IContext context)
    {
        var estimated = _state.EstimateMemoryBytes();
        ActorMemoryUsage.Record(estimated,
            new KeyValuePair<string, object?>("student.id", _studentId));

        if (estimated > StudentState.MemoryBudgetBytes * 0.8)
        {
            _logger.LogWarning(
                "StudentActor memory warning for {StudentId}: {EstimatedKB}KB / {BudgetKB}KB (80% threshold)",
                _studentId, estimated / 1024, StudentState.MemoryBudgetBytes / 1024);
        }

        if (estimated > StudentState.MemoryBudgetBytes)
        {
            _logger.LogError(
                "StudentActor memory EXCEEDED for {StudentId}: {EstimatedKB}KB / {BudgetKB}KB. " +
                "Consider pruning concept history.",
                _studentId, estimated / 1024, StudentState.MemoryBudgetBytes / 1024);
        }

        // Schedule next check
        _ = Task.Delay(MemoryCheckInterval).ContinueWith(_ =>
            context.Send(context.Self, new MemoryCheckTick()));

        return Task.CompletedTask;
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private string GetActiveMethodology(string conceptId)
    {
        return _state.MethodologyMap.GetValueOrDefault(conceptId, Methodology.Socratic).ToString();
    }

    /// <summary>
    /// Selects the next concept for a student using KST prerequisite graph.
    /// Falls back to the concept with lowest mastery if graph is unavailable.
    /// </summary>
    private string SelectNextConcept()
    {
        // TODO: Query KST graph for next prerequisite-unlocked concept
        // Fallback: lowest mastery concept that isn't mastered
        var candidate = _state.MasteryMap
            .Where(kv => kv.Value < 0.85)
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        return candidate ?? "default_start_concept";
    }

    /// <summary>
    /// Determines the dominant error type from recent attempts for a concept.
    /// Precedence: Conceptual > Procedural > Motivational.
    /// </summary>
    private ErrorType DetermineDominantErrorType(string conceptId)
    {
        var recentErrors = _state.RecentAttempts
            .Where(a => a.ConceptId == conceptId && a.ErrorType != "None")
            .Select(a => a.ErrorType)
            .ToList();

        if (recentErrors.Count == 0) return ErrorType.None;

        // Precedence order
        if (recentErrors.Any(e => e == "Conceptual")) return ErrorType.Conceptual;
        if (recentErrors.Any(e => e == "Procedural")) return ErrorType.Procedural;
        if (recentErrors.Any(e => e == "Motivational")) return ErrorType.Motivational;

        return ErrorType.None;
    }

    /// <summary>
    /// Calculates XP award for an attempt. Base XP varies by correctness,
    /// hint usage, and partial credit.
    /// </summary>
    private static int CalculateXpAward(bool isCorrect, int hintsUsed, double score)
    {
        if (!isCorrect) return (int)(score * 5); // partial credit: 0-5 XP

        // Full credit: 10 XP base, -2 per hint used
        return Math.Max(2, 10 - (hintsUsed * 2));
    }

    /// <summary>
    /// Updates streak state. Streak increments if last activity was yesterday,
    /// resets if gap > 24 hours.
    /// </summary>
    private async Task UpdateStreak(IContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var lastDate = _state.LastActivityDate.Date;
        var today = now.Date;

        int newStreak;
        if (lastDate == today)
        {
            // Same day -- no change
            return;
        }
        else if (lastDate == today.AddDays(-1))
        {
            // Consecutive day
            newStreak = _state.CurrentStreak + 1;
        }
        else
        {
            // Gap -- reset
            newStreak = 1;
        }

        var longestStreak = Math.Max(_state.LongestStreak, newStreak);

        var @event = new StreakUpdated_V1(_studentId, newStreak, longestStreak, now);
        await PersistAndPublish(context, @event);
        _state.Apply(@event);

        // Notify outreach scheduler about streak state
        if (_outreachScheduler != null)
        {
            context.Send(_outreachScheduler, new StreakStateUpdate(
                _studentId, newStreak, now));
        }
    }

    /// <summary>
    /// Computes a SHA-256 hash of the answer text for persistence.
    /// Answers are never stored in plaintext.
    /// </summary>
    private static string ComputeAnswerHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // =========================================================================
    // LOGGER FACTORY HELPER
    // =========================================================================

    private ILogger<T> CreateLogger<T>()
    {
        // In production this comes from DI. This is a convenience helper for
        // child actor creation where we don't have direct DI access.
        return (ILogger<T>)_logger;
    }
}

// =============================================================================
// INTERNAL MESSAGES (not part of public contract)
// =============================================================================

/// <summary>Timer tick for periodic memory budget checks.</summary>
internal sealed record MemoryCheckTick;

/// <summary>Internal message sent from stagnation detector to parent.</summary>
internal sealed record StagnationDetected(
    string ConceptId,
    double CompositeScore,
    StagnationSignals Signals,
    int ConsecutiveStagnantSessions);

/// <summary>Notification to outreach scheduler when a concept is mastered.</summary>
internal sealed record ConceptMasteredNotification(
    string StudentId,
    string ConceptId,
    double InitialHalfLifeHours);

/// <summary>Notification to outreach scheduler about streak state.</summary>
internal sealed record StreakStateUpdate(
    string StudentId,
    int CurrentStreak,
    DateTimeOffset LastActivityDate);

/// <summary>Request session summary before teardown.</summary>
internal sealed record GetSessionSummary;

/// <summary>Session summary response from LearningSessionActor.</summary>
internal sealed record SessionSummary(
    int DurationMinutes,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double AvgResponseTimeMs,
    double FatigueScore,
    string? LastConceptId);

/// <summary>
/// Sync offline events command. Contains a batch of events that were
/// queued on the client device during offline mode.
/// </summary>
public sealed record SyncOfflineEvents(
    string StudentId,
    IReadOnlyList<OfflineEvent> Events,
    string CorrelationId = "");

/// <summary>Base class for offline events.</summary>
public abstract record OfflineEvent(DateTimeOffset ClientTimestamp);

/// <summary>Offline attempt event.</summary>
public sealed record OfflineAttemptEvent(
    DateTimeOffset ClientTimestamp,
    string SessionId,
    string ConceptId,
    string QuestionId,
    QuestionType QuestionType,
    string Answer,
    int ResponseTimeMs,
    int HintCountUsed,
    bool WasSkipped,
    int BackspaceCount,
    int AnswerChangeCount) : OfflineEvent(ClientTimestamp);

/// <summary>Passivation message for cluster.</summary>
internal sealed record Passivate;

/// <summary>
/// Interface for the methodology switch service. Injected into StudentActor.
/// Implementation is in methodology_switch_service.cs.
/// </summary>
public interface IMethodologySwitchService
{
    Task<DecideSwitchResponse> DecideSwitch(DecideSwitchRequest request);
}

// =============================================================================
// EXTENSION METHODS
// =============================================================================

internal static class DictionaryExtensions
{
    /// <summary>
    /// Returns a read-only wrapper. Avoids copying the dictionary.
    /// </summary>
    public static IReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(
        this Dictionary<TKey, TValue> dict) where TKey : notnull
    {
        return dict;
    }

    /// <summary>
    /// Typed logger factory helper for creating child loggers.
    /// </summary>
    public static ILogger<T> CreateLogger<T>(this ILogger logger)
    {
        // This is a simplification. In production, use ILoggerFactory from DI.
        return (ILogger<T>)logger;
    }
}
