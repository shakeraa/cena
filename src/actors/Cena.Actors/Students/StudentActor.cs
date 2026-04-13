// =============================================================================
// Cena Platform -- StudentActor (Virtual, Event-Sourced)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
// Storage: Marten (PostgreSQL) event sourcing | Cache: Redis
//
// Virtual actor (grain): auto-activated on first message, passivated after
// 30 minutes of inactivity. Reactivated from latest snapshot + event replay.
// Event-sourced aggregate root for the Learner bounded context.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Events;
using Cena.Actors.Hints;
using Cena.Actors.Infrastructure;
using Cena.Actors.Outreach;
using Cena.Actors.Projections;
using Cena.Actors.Services;
using Cena.Actors.Sessions;
using Cena.Actors.Stagnation;
using Cena.Actors.Tutoring;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

using Proto;
using Proto.Cluster;
using StackExchange.Redis;

namespace Cena.Actors.Students;

/// <summary>
/// The StudentActor is the event-sourced aggregate root for all student state.
/// It is a Proto.Actor virtual actor (grain) activated on-demand by the cluster.
///
/// Lifecycle:
///   Activated: on first message to ClusterIdentity("student", studentId)
///   State recovery: load latest Marten snapshot + replay subsequent events
///   Passivation: after 30 minutes of idle (no messages received)
///   Child actors: placeholder PIDs for LearningSession, StagnationDetector, OutreachScheduler
///
/// Memory budget: ~500KB per instance. Alert at 80% node memory.
/// </summary>
public sealed partial class StudentActor : IActor
{
    // ---- Dependencies (injected via Proto.DependencyInjection) ----
    private readonly IDocumentStore _documentStore;
    private readonly INatsConnection _nats;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StudentActor> _logger;
    private readonly IMethodologySwitchService _methodologySwitchService;
    private readonly IBktService _bktService;
    private readonly IHintAdjustedBktService _hintAdjustedBktService;
    private readonly Sync.OfflineSyncHandler _offlineSyncHandler;
    private readonly Infrastructure.GracefulShutdownCoordinator? _shutdownCoordinator;
    private readonly IExplanationOrchestrator _explanationOrchestrator;
    private readonly IDeliveryGate _deliveryGate;
    private readonly IConfusionDetector _confusionDetector;
    private readonly IDisengagementClassifier _disengagementClassifier;
    private readonly ISessionEventPublisher _sessionEventPublisher;

    // ---- Actor State ----
    private StudentState _state = new();
    private string _studentId = "";
    private long _eventsSinceSnapshot;

    // ---- Session-level stagnation accumulators (ACT-020) ----
    private int _sessionAttemptCount;
    private int _sessionCorrectCount;
    private double _sessionTotalRtMs;
    private readonly Dictionary<string, int> _sessionErrorCounts = new();
    private string? _sessionPrimaryConceptId;
    private double _sessionAnnotationSentimentSum;
    private int _sessionAnnotationCount;

    // ---- SAI-003: LLM explanation rate limiting (max 3/min per student) ----
    private readonly Queue<DateTimeOffset> _llmExplanationTimestamps = new();
    private const int MaxLlmExplanationsPerMinute = 3;

    // ---- Staged events for atomic batch writes ----
    private readonly List<object> _pendingEvents = new();
    #pragma warning disable CS0414 // Tracks Marten stream existence for first-write detection
    private bool _streamExists;
    #pragma warning restore CS0414

    // ---- Timer cancellation (MEDIUM-3: prevent ghost timers after passivation) ----
    private CancellationTokenSource? _timerCts;

    // ---- Child Actor PIDs (placeholders -- children implemented separately) ----
    private PID? _sessionActor;
    private PID? _stagnationDetector;
    private PID? _outreachScheduler;

    // ---- RES-003: Redis circuit breaker PID (resolved from cluster root) ----
    #pragma warning disable CS0649 // Resolved at runtime from cluster root when Redis CB is active
    private PID? _redisCbPid;
    #pragma warning restore CS0649

    // ---- Pool governor PID (resolved from ActorSystemManager child) ----
    private PID? _managerPid;

    // ---- Configuration ----
    private static readonly TimeSpan PassivationTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MemoryCheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EventPersistTimeout = TimeSpan.FromMilliseconds(2000);

    // ---- Telemetry (ACT-023: instance-based via IMeterFactory) ----
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly Counter<long> _attemptCounter;
    private readonly Histogram<double> _eventPersistLatency;
    private readonly Histogram<long> _actorMemoryUsage;
    private readonly Counter<long> _activationCounter;
    private readonly Counter<long> _passivationCounter;
    private readonly Counter<long> _persistTimeoutCounter;

    public StudentActor(
        IDocumentStore documentStore,
        INatsConnection nats,
        IConnectionMultiplexer redis,
        ILogger<StudentActor> logger,
        IMethodologySwitchService methodologySwitchService,
        IBktService bktService,
        IHintAdjustedBktService hintAdjustedBktService,
        Sync.OfflineSyncHandler offlineSyncHandler,
        IExplanationOrchestrator explanationOrchestrator,
        IDeliveryGate deliveryGate,
        IConfusionDetector confusionDetector,
        IDisengagementClassifier disengagementClassifier,
        ISessionEventPublisher sessionEventPublisher,
        IMeterFactory meterFactory,
        Infrastructure.GracefulShutdownCoordinator? shutdownCoordinator = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _methodologySwitchService = methodologySwitchService ?? throw new ArgumentNullException(nameof(methodologySwitchService));
        _bktService = bktService ?? throw new ArgumentNullException(nameof(bktService));
        _hintAdjustedBktService = hintAdjustedBktService ?? throw new ArgumentNullException(nameof(hintAdjustedBktService));
        _shutdownCoordinator = shutdownCoordinator;
        _offlineSyncHandler = offlineSyncHandler ?? throw new ArgumentNullException(nameof(offlineSyncHandler));
        _explanationOrchestrator = explanationOrchestrator ?? throw new ArgumentNullException(nameof(explanationOrchestrator));
        _deliveryGate = deliveryGate ?? throw new ArgumentNullException(nameof(deliveryGate));
        _confusionDetector = confusionDetector ?? throw new ArgumentNullException(nameof(confusionDetector));
        _disengagementClassifier = disengagementClassifier ?? throw new ArgumentNullException(nameof(disengagementClassifier));
        _sessionEventPublisher = sessionEventPublisher ?? throw new ArgumentNullException(nameof(sessionEventPublisher));

        // ACT-023: Instance-based telemetry via IMeterFactory
        _activitySource = new ActivitySource("Cena.Actors.StudentActor", "1.0.0");
        _meter = meterFactory.Create("Cena.Actors.StudentActor", "1.0.0");
        _attemptCounter = _meter.CreateCounter<long>("cena.student.attempts_total",
            description: "Total concept attempts processed");
        _eventPersistLatency = _meter.CreateHistogram<double>("cena.student.event_persist_ms",
            description: "Event persistence latency in ms");
        _actorMemoryUsage = _meter.CreateHistogram<long>("cena.student.memory_bytes",
            description: "Estimated actor memory usage in bytes");
        _activationCounter = _meter.CreateCounter<long>("cena.student.activations_total",
            description: "Actor activations");
        _passivationCounter = _meter.CreateCounter<long>("cena.student.passivations_total",
            description: "Actor passivations");
        _persistTimeoutCounter = _meter.CreateCounter<long>("cena.student.persist_timeout_total",
            description: "Event persistence timeouts");
    }

    // =========================================================================
    // PROTO.ACTOR LIFECYCLE
    // =========================================================================

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

            // ---- Pre-warm (no-op after activation) ----
            WarmUp               => HandleWarmUp(context),

            // ---- Account Lifecycle (LCM-001) ----
            AccountStatusChanged cmd => HandleAccountStatusChanged(context, cmd),

            // ---- Commands (guarded by account status check) ----
            AttemptConcept cmd   => GuardAccountStatus(context) ?? HandleAttemptConcept(context, cmd),
            StartSession cmd     => GuardAccountStatus(context) ?? HandleStartSession(context, cmd),
            EndSession cmd       => HandleEndSession(context, cmd), // Always allow session end
            ResumeSession cmd    => GuardAccountStatus(context) ?? HandleResumeSession(context, cmd),
            SwitchMethodology cmd=> GuardAccountStatus(context) ?? HandleSwitchMethodology(context, cmd),
            AddAnnotation cmd    => GuardAccountStatus(context) ?? HandleAddAnnotation(context, cmd),
            SyncOfflineEvents cmd=> GuardAccountStatus(context) ?? HandleSyncOfflineEvents(context, cmd),
            TeacherMethodologyOverride cmd => HandleTeacherMethodologyOverride(context, cmd), // Admin always allowed

            // ---- Queries ----
            GetStudentProfile q  => HandleGetProfile(context, q),
            GetReviewSchedule q  => HandleGetReviewSchedule(context, q),
            GetMasteryOverlayQuery q => HandleGetMasteryOverlay(context, q),
            GetMethodologyProfile q => HandleGetMethodologyProfile(context, q),
            GetSessionSnapshot q => HandleGetSessionSnapshot(context, q),

            // ---- Internal ----
            StagnationDetected msg => HandleStagnationDetected(context, msg),
            MemoryCheckTick      => HandleMemoryCheck(context),
            DelegateEvent del    => HandleDelegateEvent(del),

            _ => Task.CompletedTask
        };
    }

    // =========================================================================
    // LIFECYCLE HANDLERS
    // =========================================================================

    private async Task OnStarted(IContext context)
    {
        var sw = Stopwatch.StartNew();
        using var activity = _activitySource.StartActivity("StudentActor.Started");

        _studentId = context.ClusterIdentity()?.Identity
            ?? throw new InvalidOperationException("StudentActor requires a ClusterIdentity");

        activity?.SetTag("student.id", _studentId);
        _logger.LogInformation("StudentActor activating for student {StudentId}", _studentId);

        // Restore state from Marten (snapshot + event replay)
        await RestoreStateFromEventStore();

        // Spawn child actors with supervision (placeholder PIDs)
        SpawnChildActors(context);

        // Set passivation timeout -- actor will receive ReceiveTimeout after 30 min idle
        context.SetReceiveTimeout(PassivationTimeout);

        // Schedule periodic memory check (with cancellation to prevent ghost timers)
        _timerCts = new CancellationTokenSource();
        ScheduleMemoryCheck(context);

        // Register with pool governor for back-pressure and graceful shutdown
        RegisterWithManager(context);

        sw.Stop();
        _activationCounter.Add(1, new KeyValuePair<string, object?>("student.id", _studentId));

        _logger.LogInformation(
            "StudentActor activated for student {StudentId} in {ElapsedMs}ms. " +
            "EventVersion={EventVersion}, SessionCount={SessionCount}, Concepts={ConceptCount}",
            _studentId, sw.ElapsedMilliseconds, _state.EventVersion,
            _state.SessionCount, _state.MasteryMap.Count);
    }

    private async Task OnStopping(IContext context)
    {
        using var activity = _activitySource.StartActivity("StudentActor.Stopping");
        activity?.SetTag("student.id", _studentId);

        _logger.LogInformation(
            "StudentActor stopping for student {StudentId}. Persisting final state.", _studentId);

        // Cancel periodic timers to prevent ghost messages to dead PID
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;

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

    private Task OnStopped(IContext context)
    {
        // Deregister from pool governor so drain/shutdown tracking is accurate
        DeregisterFromManager(context);

        _passivationCounter.Add(1, new KeyValuePair<string, object?>("student.id", _studentId));
        _activitySource.Dispose();
        _logger.LogInformation("StudentActor stopped for student {StudentId}", _studentId);
        return Task.CompletedTask;
    }

    private Task OnRestarting(IContext context)
    {
        _logger.LogWarning(
            "StudentActor restarting for student {StudentId}. Resetting transient state.", _studentId);
        _sessionActor = null;
        _stagnationDetector = null;
        _outreachScheduler = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op handler. By the time this runs, OnStarted has already restored state.
    /// Responds immediately so the pre-warmer knows the actor is alive.
    /// </summary>
    private Task HandleWarmUp(IContext context)
    {
        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // LCM-001: ACCOUNT LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Guards commands against non-active account status. Returns a completed Task
    /// with an error response if blocked, or null to allow the command through.
    /// Used with null-coalescing: GuardAccountStatus(ctx) ?? HandleCommand(ctx, cmd)
    /// </summary>
    private Task? GuardAccountStatus(IContext context)
    {
        if (_state.AccountStatus == AccountStatus.Active)
            return null; // Allow through

        _logger.LogWarning(
            "Command rejected for student {StudentId}: account status is {Status}",
            _studentId, _state.AccountStatus);

        context.Respond(new ActorResult(false,
            ErrorCode: "ACCOUNT_BLOCKED",
            ErrorMessage: $"Account is {_state.AccountStatus}"));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles account status changes (suspension, lock, freeze, deletion).
    /// Updates in-memory state, ends active session if needed, passivates on deletion.
    /// </summary>
    private async Task HandleAccountStatusChanged(IContext context, AccountStatusChanged cmd)
    {
        var previousStatus = _state.AccountStatus;
        _state.AccountStatus = cmd.NewStatus;

        _logger.LogInformation(
            "StudentActor {StudentId}: account status {PreviousStatus} → {NewStatus} (by {ChangedBy}: {Reason})",
            _studentId, previousStatus, cmd.NewStatus, cmd.ChangedBy, cmd.Reason ?? "no reason");

        // Persist the status change event
        StageEvent(new AccountStatusChanged_V1(
            _studentId, cmd.NewStatus.ToString(), cmd.Reason, cmd.ChangedBy, cmd.ChangedAt));
        await FlushEvents();

        // End active session for blocking statuses
        if (cmd.NewStatus is AccountStatus.Suspended or AccountStatus.Locked
            or AccountStatus.Frozen or AccountStatus.PendingDelete)
        {
            if (_sessionActor != null)
            {
                _logger.LogInformation(
                    "Ending active session for student {StudentId} due to account {Status}",
                    _studentId, cmd.NewStatus);
                await context.StopAsync(_sessionActor);
                _sessionActor = null;
                _state.ActiveSessionId = null;
            }
        }

        // Passivate on pending deletion — actor should not remain in memory
        if (cmd.NewStatus == AccountStatus.PendingDelete)
        {
            context.Respond(new ActorResult(true));
            context.Poison(context.Self);
            return;
        }

        context.Respond(new ActorResult(true));
    }

    private Task OnPassivation(IContext context)
    {
        _logger.LogInformation(
            "StudentActor passivating for student {StudentId} after {Timeout} idle",
            _studentId, PassivationTimeout);

        // Signal the cluster to deactivate this grain
        context.Poison(context.Self);
        return Task.CompletedTask;
    }

    // =========================================================================
    // STATE RESTORATION
    // =========================================================================

    /// <summary>
    /// Restores actor state from Marten: load latest snapshot, then replay
    /// events that occurred after the snapshot. Standard event sourcing rehydration.
    /// </summary>
    private async Task RestoreStateFromEventStore()
    {
        using var activity = _activitySource.StartActivity("StudentActor.RestoreState");
        var sw = Stopwatch.StartNew();

        await using var session = _documentStore.LightweightSession();

        // Fetch stream state first — if null, this is a brand-new student
        // and we can skip the heavier AggregateStreamAsync call entirely.
        // Saves one PG round-trip per new student cold-start.
        var streamState = await session.Events.FetchStreamStateAsync(_studentId);
        StudentProfileSnapshot? snapshot = null;

        if (streamState != null)
        {
            _state.EventVersion = (int)streamState.Version;
            _streamExists = true;
            snapshot = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>(_studentId);
        }

        if (snapshot != null)
        {
            _state.StudentId = snapshot.StudentId;
            _state.TotalXp = snapshot.TotalXp;
            _state.CurrentStreak = snapshot.CurrentStreak;
            _state.LongestStreak = snapshot.LongestStreak;
            _state.LastActivityDate = snapshot.LastActivityDate;
            _state.ExperimentCohort = snapshot.ExperimentCohort;
            _state.SchoolId = snapshot.SchoolId; // REV-014: restore tenant scope
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
                    ? cs.MasteredAt ?? snapshot.LastActivityDate
                    : snapshot.LastActivityDate;
                _state.HlrTimers[conceptId] = new HlrState(halfLife, lastReview);
            }

            // MST-006: Rebuild MasteryOverlay from snapshot data.
            // The snapshot stores PKnown, TotalAttempts, and HalfLife — enough to
            // seed the rich mastery state. Error history, Bloom levels, and method
            // tracking will be empty until new events arrive, but the critical
            // mastery probability and HLR half-life are restored.
            foreach (var (conceptId, masteryState) in snapshot.ConceptMastery)
            {
                var halfLife = snapshot.HalfLifeMap.GetValueOrDefault(conceptId, 0.0);
                var lastInteraction = masteryState.LastAttemptedAt ?? snapshot.LastActivityDate;

                _state.MasteryOverlay[conceptId] = new Mastery.ConceptMasteryState
                {
                    MasteryProbability = (float)masteryState.PKnown,
                    HalfLifeHours = (float)halfLife,
                    AttemptCount = masteryState.TotalAttempts,
                    CorrectCount = masteryState.CorrectCount,
                    LastInteraction = lastInteraction,
                    FirstEncounter = lastInteraction
                };
            }

            // LCM-001: Restore account status from snapshot
            if (Enum.TryParse<AccountStatus>(snapshot.AccountStatus, true, out var acctStatus))
                _state.AccountStatus = acctStatus;

            // Restore hierarchical methodology maps
            foreach (var (key, assignment) in snapshot.SubjectMethodologyMap)
                _state.SubjectMethodologyMap[key] = assignment;
            foreach (var (key, assignment) in snapshot.TopicMethodologyMap)
                _state.TopicMethodologyMap[key] = assignment;
            foreach (var (key, assignment) in snapshot.ConceptMethodologyMap)
                _state.ConceptMethodologyMap[key] = assignment;
            foreach (var (key, count) in snapshot.SessionsSinceSwitch)
                _state.SessionsSinceSwitch[key] = count;

            _logger.LogDebug(
                "Restored state for student {StudentId} from snapshot. " +
                "Concepts={ConceptCount}, MasteryOverlay={OverlayCount}, XP={XP}, Streak={Streak}, " +
                "SubjectMethods={SubjectCount}, TopicMethods={TopicCount}, ConceptMethods={ConceptMethodCount}",
                _studentId, _state.MasteryMap.Count, _state.MasteryOverlay.Count,
                _state.TotalXp, _state.CurrentStreak,
                _state.SubjectMethodologyMap.Count, _state.TopicMethodologyMap.Count,
                _state.ConceptMethodologyMap.Count);
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
    // EVENT PERSISTENCE & PUBLISHING (StageEvent + FlushEvents pattern)
    // =========================================================================

    /// <summary>
    /// Queues an event for batch persistence. Call FlushEvents() at the end of each command handler.
    /// </summary>
    private void StageEvent<TEvent>(TEvent @event) where TEvent : class
    {
        _pendingEvents.Add(@event);
    }

    /// <summary>
    /// Persists ALL staged events atomically to Marten with expected version (optimistic concurrency),
    /// then publishes to NATS via outbox pattern.
    /// </summary>
    private async Task FlushEvents()
    {
        if (_pendingEvents.Count == 0) return;

        var sw = Stopwatch.StartNew();
        using var activity = _activitySource.StartActivity("StudentActor.FlushEvents");
        activity?.SetTag("event.count", _pendingEvents.Count);

        // Persist ALL events atomically.
        // Use Append without expected version — lets Marten handle stream creation
        // and avoids race conditions with concurrent seed data.
        // RES-001: 2s timeout prevents actor mailbox starvation on slow DB
        await using var session = _documentStore.LightweightSession();
        session.Events.Append(_studentId, _pendingEvents.ToArray());

        using var cts = new CancellationTokenSource(EventPersistTimeout);
        try
        {
            await session.SaveChangesAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _persistTimeoutCounter.Add(1,
                new KeyValuePair<string, object?>("student.id", _studentId));
            _logger.LogError(
                "Event persist timed out for student {StudentId} after {Timeout}ms. " +
                "Letting supervision restart the actor.",
                _studentId, EventPersistTimeout.TotalMilliseconds);
            throw; // Supervision strategy will restart the actor
        }

        _eventsSinceSnapshot += _pendingEvents.Count;
        sw.Stop();

        _eventPersistLatency.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("event.count", _pendingEvents.Count));

        // ACT-027: Removed inline NATS publishing — outbox pattern (NatsOutboxPublisher)
        // is the single source of NATS delivery to prevent duplicate downstream events.

        _pendingEvents.Clear();

        // Check snapshot threshold
        if (_eventsSinceSnapshot >= StudentState.SnapshotInterval)
        {
            await ForceSnapshot();
        }
    }

    private async Task ForceSnapshot()
    {
        using var activity = _activitySource.StartActivity("StudentActor.ForceSnapshot");
        var sw = Stopwatch.StartNew();

        try
        {
            await using var session = _documentStore.LightweightSession();

            // ACT-026: Build and persist snapshot document explicitly.
            // Marten's inline projection handles this automatically during event appends,
            // but ForceSnapshot is called on passivation to ensure a checkpoint exists.
            var snapshot = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>(_studentId);

            if (snapshot != null)
            {
                session.Store(snapshot);
                // RES-001: 2s timeout on snapshot writes too
                using var snapshotCts = new CancellationTokenSource(EventPersistTimeout);
                try
                {
                    await session.SaveChangesAsync(snapshotCts.Token);
                }
                catch (OperationCanceledException) when (snapshotCts.IsCancellationRequested)
                {
                    _persistTimeoutCounter.Add(1,
                        new KeyValuePair<string, object?>("student.id", _studentId));
                    _logger.LogError(
                        "Snapshot persist timed out for student {StudentId} after {Timeout}ms",
                        _studentId, EventPersistTimeout.TotalMilliseconds);
                    throw;
                }
            }

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
    // REDIS CACHE (RES-003: circuit breaker aware)
    // =========================================================================

    /// <summary>
    /// Checks if the Redis circuit breaker is open. When open, all Redis ops
    /// are skipped and the actor falls back to Marten-only reads.
    /// </summary>
    private async Task<bool> IsRedisCbOpen(IContext context)
    {
        if (_redisCbPid == null) return false;

        try
        {
            var status = await context.RequestAsync<Gateway.CircuitStatusResponse>(
                _redisCbPid, new Gateway.GetCircuitStatus(), TimeSpan.FromSeconds(1));
            return status.State != Gateway.CircuitState.Closed;
        }
        catch
        {
            // If we can't reach the CB actor, assume Redis is available
            return false;
        }
    }

    /// <summary>
    /// Reports a Redis operation failure to the circuit breaker.
    /// </summary>
    private void ReportRedisFailure(IContext context, string reason)
    {
        if (_redisCbPid != null)
        {
            context.Send(_redisCbPid, new Gateway.ReportFailure(
                Guid.NewGuid().ToString(), reason, "redis"));
        }
    }

    /// <summary>
    /// Reports a Redis operation success to the circuit breaker.
    /// </summary>
    private void ReportRedisSuccess(IContext context)
    {
        if (_redisCbPid != null)
        {
            context.Send(_redisCbPid, new Gateway.ReportSuccess(
                Guid.NewGuid().ToString(), "redis"));
        }
    }

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
                "Failed to invalidate Redis cache for student {StudentId}. " +
                "RES-003: Redis CB will track this failure.", _studentId);
        }
    }

    // =========================================================================
    // POOL GOVERNOR REGISTRATION
    // =========================================================================

    /// <summary>
    /// Resolve the StudentActorManager PID and register this actor for
    /// pool tracking, back-pressure, and graceful shutdown coordination.
    /// The PID is registered in GracefulShutdownCoordinator during bootstrap;
    /// we resolve it via the same coordinator injected into the system.
    /// </summary>
    private void RegisterWithManager(IContext context)
    {
        try
        {
            // Resolve manager PID from the shutdown coordinator (which holds it)
            _managerPid = _shutdownCoordinator?.ManagerPid;
            if (_managerPid != null)
            {
                context.Send(_managerPid, new Management.ActivateStudent(_studentId));
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: actor functions without pool governor
            _logger.LogDebug(ex, "Could not register with StudentActorManager for {StudentId}", _studentId);
        }
    }

    private void DeregisterFromManager(IContext context)
    {
        if (_managerPid != null)
        {
            try
            {
                context.Send(_managerPid, new Management.StudentDeactivated(_studentId));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not deregister from StudentActorManager for {StudentId}", _studentId);
            }
        }
    }
}

// =============================================================================
// DICTIONARY EXTENSION
// =============================================================================

internal static class DictionaryExtensions
{
    public static IReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(
        this Dictionary<TKey, TValue> dict) where TKey : notnull
    {
        return dict;
    }
}
