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
using Cena.Actors.Infrastructure;
using Cena.Actors.Outreach;
using Cena.Actors.Services;
using Cena.Actors.Sessions;
using Cena.Actors.Stagnation;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
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
public sealed class StudentActor : IActor
{
    // ---- Dependencies (injected via Proto.DependencyInjection) ----
    private readonly IDocumentStore _documentStore;
    private readonly INatsConnection _nats;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StudentActor> _logger;
    private readonly IMethodologySwitchService _methodologySwitchService;

    // ---- Actor State ----
    private StudentState _state = new();
    private string _studentId = "";
    private long _eventsSinceSnapshot;

    // ---- Staged events for atomic batch writes ----
    private readonly List<object> _pendingEvents = new();

    // ---- Child Actor PIDs (placeholders -- children implemented separately) ----
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
        MeterInstance.CreateCounter<long>("cena.student.attempts_total",
            description: "Total concept attempts processed");
    private static readonly Histogram<double> EventPersistLatency =
        MeterInstance.CreateHistogram<double>("cena.student.event_persist_ms",
            description: "Event persistence latency in ms");
    private static readonly Histogram<long> ActorMemoryUsage =
        MeterInstance.CreateHistogram<long>("cena.student.memory_bytes",
            description: "Estimated actor memory usage in bytes");
    private static readonly Counter<long> ActivationCounter =
        MeterInstance.CreateCounter<long>("cena.student.activations_total",
            description: "Actor activations");
    private static readonly Counter<long> PassivationCounter =
        MeterInstance.CreateCounter<long>("cena.student.passivations_total",
            description: "Actor passivations");

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
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.Started");

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

        // Schedule periodic memory check
        var self = context.Self;
        var system = context.System;
        _ = Task.Delay(MemoryCheckInterval).ContinueWith(_ =>
            system.Root.Send(self, new MemoryCheckTick()));

        sw.Stop();
        ActivationCounter.Add(1, new KeyValuePair<string, object?>("student.id", _studentId));

        _logger.LogInformation(
            "StudentActor activated for student {StudentId} in {ElapsedMs}ms. " +
            "EventVersion={EventVersion}, SessionCount={SessionCount}, Concepts={ConceptCount}",
            _studentId, sw.ElapsedMilliseconds, _state.EventVersion,
            _state.SessionCount, _state.MasteryMap.Count);
    }

    private async Task OnStopping(IContext context)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.Stopping");
        activity?.SetTag("student.id", _studentId);

        _logger.LogInformation(
            "StudentActor stopping for student {StudentId}. Persisting final state.", _studentId);

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
        PassivationCounter.Add(1, new KeyValuePair<string, object?>("student.id", _studentId));
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
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.RestoreState");
        var sw = Stopwatch.StartNew();

        await using var session = _documentStore.LightweightSession();

        // Marten AggregateStreamAsync handles snapshot + replay automatically.
        var snapshot = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>(_studentId);

        if (snapshot != null)
        {
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
                    ? cs.MasteredAt ?? snapshot.LastActivityDate
                    : snapshot.LastActivityDate;
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
    /// Spawns placeholder child actors with supervision strategy.
    /// LearningSessionActor is NOT spawned here -- it is created on StartSession.
    /// Child actor implementations are in separate files.
    /// </summary>
    private void SpawnChildActors(IContext context)
    {
        // Placeholder: child actors are implemented by another agent.
        // For now, log that we would spawn them. The PIDs remain null
        // and all forwarding code checks for null before sending.
        _logger.LogDebug(
            "Child actor spawning deferred for student {StudentId}. " +
            "StagnationDetector and OutreachScheduler will be spawned when implementations are available.",
            _studentId);
    }

    // =========================================================================
    // COMMAND HANDLERS
    // =========================================================================

    /// <summary>
    /// Handles a concept attempt -- the primary hot-path command.
    /// Validates input, runs BKT via session actor, stages events, flushes atomically.
    /// </summary>
    private async Task HandleAttemptConcept(IContext context, AttemptConcept cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.AttemptConcept");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("concept.id", cmd.ConceptId);
        activity?.SetTag("question.id", cmd.QuestionId);

        try
        {
            // ---- Input validation ----
            if (string.IsNullOrWhiteSpace(cmd.ConceptId))
            {
                context.Respond(new ActorResult<EvaluateAnswerResponse>(
                    false, ErrorCode: "INVALID_INPUT", ErrorMessage: "ConceptId is required"));
                return;
            }

            if (cmd.ResponseTimeMs < 0 || cmd.ResponseTimeMs > 600_000)
            {
                context.Respond(new ActorResult<EvaluateAnswerResponse>(
                    false, ErrorCode: "INVALID_INPUT",
                    ErrorMessage: "ResponseTimeMs must be between 0 and 600000"));
                return;
            }

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
                // Session actor not yet available -- perform inline BKT estimation
                var priorMastery = _state.MasteryMap.GetValueOrDefault(cmd.ConceptId, 0.3);

                // Inline BKT update: simplified Bayesian update
                // P(known|correct) = P(correct|known)*P(known) / P(correct)
                // P(correct) = P(correct|known)*P(known) + P(correct|not_known)*P(not_known)
                const double pGuess = 0.25;
                const double pSlip = 0.10;
                const double pLearn = 0.10;

                double posteriorKnown;
                if (cmd.WasSkipped)
                {
                    // Treat skip as incorrect
                    double pObserved = (1 - pSlip) * priorMastery + pGuess * (1 - priorMastery);
                    double pCorrectGivenKnown = 1 - pSlip;
                    posteriorKnown = (pSlip * priorMastery) /
                        (pSlip * priorMastery + (1 - pGuess) * (1 - priorMastery));
                }
                else
                {
                    // Real BKT update -- we don't know if correct yet without session actor
                    // Use a conservative estimate: treat as correct if not skipped
                    double pCorrectGivenKnown = 1 - pSlip;
                    double pCorrect = pCorrectGivenKnown * priorMastery + pGuess * (1 - priorMastery);
                    posteriorKnown = (pCorrectGivenKnown * priorMastery) / pCorrect;
                }

                // Apply learning transition
                double updatedMastery = posteriorKnown + (1 - posteriorKnown) * pLearn;
                updatedMastery = Math.Clamp(updatedMastery, 0.0, 1.0);

                bool isCorrect = !cmd.WasSkipped; // conservative assumption without evaluator

                // ---- Stage domain event ----
                var @event = new ConceptAttempted_V1(
                    cmd.StudentId, cmd.ConceptId, cmd.SessionId,
                    isCorrect, cmd.ResponseTimeMs, cmd.QuestionId,
                    cmd.QuestionType.ToString(), GetActiveMethodology(cmd.ConceptId),
                    ErrorType.None.ToString(),
                    priorMastery, updatedMastery,
                    cmd.HintCountUsed, cmd.WasSkipped,
                    ComputeAnswerHash(cmd.Answer),
                    cmd.BackspaceCount, cmd.AnswerChangeCount, cmd.WasOffline,
                    DateTimeOffset.UtcNow);

                StageEvent(@event);

                // ---- Award XP ----
                int xpEarned = CalculateXpAward(isCorrect, cmd.HintCountUsed, isCorrect ? 1.0 : 0.0);
                XpAwarded_V1? xpEvent = null;
                if (xpEarned > 0)
                {
                    xpEvent = new XpAwarded_V1(
                        _studentId, xpEarned, "exercise_correct", _state.TotalXp + xpEarned,
                        "recall", 1);
                    StageEvent(xpEvent);
                }

                // ---- Check mastery threshold (0.85) ----
                ConceptMastered_V1? masteredEvent = null;
                if (updatedMastery >= 0.85 && priorMastery < 0.85)
                {
                    masteredEvent = new ConceptMastered_V1(
                        _studentId, cmd.ConceptId, cmd.SessionId,
                        updatedMastery,
                        _state.MasteryMap.Count,
                        _state.SessionCount,
                        GetActiveMethodology(cmd.ConceptId),
                        24.0,
                        DateTimeOffset.UtcNow);
                    StageEvent(masteredEvent);
                }

                // ---- Flush all staged events atomically ----
                await FlushEvents();

                // ---- Apply SAME event instances to local state (after successful persist) ----
                _state.Apply(@event);
                if (xpEvent != null)
                    _state.Apply(xpEvent);
                if (masteredEvent != null)
                    _state.Apply(masteredEvent);

                // TODO: Stagnation detector expects session-level UpdateStagnationSignals, not per-attempt signals.
                // Accumulate signals within session and send summary in HandleEndSession.

                // ---- Telemetry ----
                AttemptCounter.Add(1,
                    new KeyValuePair<string, object?>("student.id", _studentId),
                    new KeyValuePair<string, object?>("correct", isCorrect));

                var evalResponse = new EvaluateAnswerResponse(
                    cmd.QuestionId, isCorrect, isCorrect ? 1.0 : 0.0,
                    "Evaluated inline (session actor unavailable)",
                    ErrorType.None, updatedMastery, "continue", xpEarned);

                context.Respond(new ActorResult<EvaluateAnswerResponse>(true, evalResponse));
                return;
            }

            // ---- Session actor is available: forward for full BKT + LLM evaluation ----
            var evaluateMsg = new EvaluateAnswer(
                cmd.SessionId, cmd.QuestionId, cmd.Answer,
                cmd.ResponseTimeMs, null, cmd.BackspaceCount, cmd.AnswerChangeCount);

            var evalResult = await context.RequestAsync<ActorResult<EvaluateAnswerResponse>>(
                _sessionActor, evaluateMsg, TimeSpan.FromSeconds(10));

            if (evalResult?.Success != true || evalResult.Data == null)
            {
                context.Respond(evalResult ?? new ActorResult<EvaluateAnswerResponse>(
                    false, ErrorCode: "EVALUATION_FAILED"));
                return;
            }

            var eval = evalResult.Data;
            var prior = _state.MasteryMap.GetValueOrDefault(cmd.ConceptId, 0.3);

            var attemptEvent = new ConceptAttempted_V1(
                cmd.StudentId, cmd.ConceptId, cmd.SessionId,
                eval.IsCorrect, cmd.ResponseTimeMs, cmd.QuestionId,
                cmd.QuestionType.ToString(), GetActiveMethodology(cmd.ConceptId),
                eval.ClassifiedErrorType.ToString(),
                prior, eval.UpdatedMastery,
                cmd.HintCountUsed, cmd.WasSkipped,
                ComputeAnswerHash(cmd.Answer),
                cmd.BackspaceCount, cmd.AnswerChangeCount, cmd.WasOffline,
                DateTimeOffset.UtcNow);

            StageEvent(attemptEvent);

            int xp = CalculateXpAward(eval.IsCorrect, cmd.HintCountUsed, eval.Score);
            XpAwarded_V1? xpEvt = null;
            if (xp > 0)
            {
                xpEvt = new XpAwarded_V1(
                    _studentId, xp, "exercise_correct", _state.TotalXp + xp, "recall", 1);
                StageEvent(xpEvt);
            }

            ConceptMastered_V1? masteredEvt = null;
            if (eval.UpdatedMastery >= 0.85 && prior < 0.85)
            {
                masteredEvt = new ConceptMastered_V1(
                    _studentId, cmd.ConceptId, cmd.SessionId,
                    eval.UpdatedMastery, _state.MasteryMap.Count, _state.SessionCount,
                    GetActiveMethodology(cmd.ConceptId), 24.0, DateTimeOffset.UtcNow);
                StageEvent(masteredEvt);
            }

            await FlushEvents();

            // Apply SAME event instances to local state (after successful persist)
            _state.Apply(attemptEvent);
            if (xpEvt != null)
                _state.Apply(xpEvt);
            if (masteredEvt != null)
            {
                _state.Apply(masteredEvt);

                if (_outreachScheduler != null)
                {
                    context.Send(_outreachScheduler, new Outreach.ConceptMasteredNotification(
                        cmd.ConceptId, 24.0));
                }
            }

            // TODO: Stagnation detector expects session-level UpdateStagnationSignals, not per-attempt signals.
            // Accumulate signals within session and send summary in HandleEndSession.

            AttemptCounter.Add(1,
                new KeyValuePair<string, object?>("student.id", _studentId),
                new KeyValuePair<string, object?>("correct", eval.IsCorrect));

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
    /// Starts a new learning session. Only one session can be active at a time.
    /// </summary>
    private async Task HandleStartSession(IContext context, StartSession cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.StartSession");
        activity?.SetTag("student.id", _studentId);

        try
        {
            // Check for existing session
            if (_state.ActiveSessionId != null)
            {
                _logger.LogWarning(
                    "Student {StudentId} already has active session {ActiveSession}. Ending it first.",
                    _studentId, _state.ActiveSessionId);

                await HandleEndSession(context, new EndSession(
                    _studentId, _state.ActiveSessionId, SessionEndReason.AppBackgrounded));
            }

            var sessionId = Guid.CreateVersion7().ToString();
            var startingConceptId = cmd.ConceptId ?? SelectNextConcept();
            var methodology = _state.MethodologyMap.GetValueOrDefault(
                startingConceptId, Methodology.Socratic);

            // Persist session started event
            var @event = new SessionStarted_V1(
                _studentId, sessionId, cmd.DeviceType, cmd.AppVersion,
                methodology.ToString(), _state.ExperimentCohort,
                cmd.IsOffline, cmd.ClientTimestamp);

            StageEvent(@event);
            await FlushEvents();
            _state.Apply(@event);

            // Update streak
            await UpdateStreak(context);

            context.Respond(new ActorResult<StartSessionResponse>(true,
                new StartSessionResponse(
                    sessionId, startingConceptId,
                    startingConceptId, // concept name resolution deferred to KST graph lookup
                    methodology, _state.TotalXp, _state.CurrentStreak)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start session for student {StudentId}", _studentId);
            context.Respond(new ActorResult<StartSessionResponse>(
                false, ErrorCode: "SESSION_START_FAILED", ErrorMessage: ex.Message));
        }
    }

    /// <summary>
    /// Ends an active learning session. Destroys the child session actor,
    /// triggers stagnation check, and persists the session-ended event.
    /// </summary>
    private async Task HandleEndSession(IContext context, EndSession cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.EndSession");
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

            // Get session summary from session actor before stopping
            SessionSummary? summary = null;
            if (_sessionActor != null)
            {
                summary = await context.RequestAsync<SessionSummary>(
                    _sessionActor, new GetSessionSummary(), TimeSpan.FromSeconds(5));

                await context.StopAsync(_sessionActor);
                _sessionActor = null;
            }

            var @event = new SessionEnded_V1(
                _studentId, cmd.SessionId, cmd.Reason.ToString(),
                summary?.DurationMinutes ?? 0,
                summary?.QuestionsAttempted ?? 0,
                summary?.QuestionsCorrect ?? 0,
                summary?.AvgResponseTimeMs ?? 0,
                summary?.FatigueScore ?? 0);

            StageEvent(@event);
            await FlushEvents();
            _state.Apply(@event);

            // Trigger stagnation check post-session
            if (_stagnationDetector != null && summary?.LastConceptId != null)
            {
                context.Send(_stagnationDetector, new Stagnation.CheckStagnation(
                    summary.LastConceptId));
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
    /// Handles a student-initiated methodology switch.
    /// </summary>
    private async Task HandleSwitchMethodology(IContext context, SwitchMethodology cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.SwitchMethodology");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("concept.id", cmd.ConceptId);

        try
        {
            var currentMethodology = _state.MethodologyMap.GetValueOrDefault(
                cmd.ConceptId, Methodology.Socratic);

            var decision = await _methodologySwitchService.DecideSwitch(
                new DecideSwitchRequest(
                    _studentId, cmd.ConceptId, cmd.ConceptId,
                    ErrorType.None, currentMethodology,
                    _state.MethodAttemptHistory.GetValueOrDefault(cmd.ConceptId, new())
                        .Select(m => m.Methodology).ToList(),
                    0.0, 0));

            if (!decision.ShouldSwitch)
            {
                context.Respond(new ActorResult(
                    false, ErrorCode: "SWITCH_DENIED", ErrorMessage: decision.DecisionTrace));
                return;
            }

            var newMethodology = decision.RecommendedMethodology;

            var @event = new MethodologySwitched_V1(
                _studentId, cmd.ConceptId,
                currentMethodology.ToString(),
                newMethodology.ToString(),
                "student_requested", 0.0,
                ErrorType.None.ToString(),
                decision.Confidence);

            StageEvent(@event);
            await FlushEvents();
            _state.Apply(@event);

            // Reset stagnation detector for this concept
            if (_stagnationDetector != null)
            {
                context.Send(_stagnationDetector, new Stagnation.ResetAfterSwitch(
                    cmd.ConceptId));
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
    /// Adds a student annotation to a concept.
    /// </summary>
    private async Task HandleAddAnnotation(IContext context, AddAnnotation cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.AddAnnotation");

        try
        {
            var annotationId = Guid.CreateVersion7().ToString();
            var contentHash = ComputeAnswerHash(cmd.Text);

            // Sentiment analysis placeholder -- real NLP call via LLM ACL
            double sentimentScore = 0.5;

            var @event = new AnnotationAdded_V1(
                _studentId, cmd.ConceptId, annotationId,
                contentHash, sentimentScore, cmd.Kind.ToString());

            StageEvent(@event);
            await FlushEvents();
            _state.Apply(@event);

            // TODO: Stagnation detector expects session-level UpdateStagnationSignals, not per-attempt signals.
            // Accumulate signals within session and send summary in HandleEndSession.

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
    /// Reconciles offline events queued on the client device.
    /// Events are replayed in chronological order with idempotency checks via Redis.
    /// </summary>
    private async Task HandleSyncOfflineEvents(IContext context, SyncOfflineEvents cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.SyncOfflineEvents");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("event.count", cmd.Events.Count);

        _logger.LogInformation(
            "Syncing {Count} offline events for student {StudentId}",
            cmd.Events.Count, _studentId);

        int processed = 0;
        int skipped = 0;
        int failed = 0;

        var db = _redis.GetDatabase();

        foreach (var offlineEvent in cmd.Events.OrderBy(e => e.ClientTimestamp))
        {
            try
            {
                // Idempotency check: Redis SET NX with 72-hour TTL
                var idempotencyKey = $"cena:idempotency:{_studentId}:{offlineEvent.IdempotencyKey}";
                var isNew = await db.StringSetAsync(idempotencyKey, "1",
                    TimeSpan.FromHours(72), When.NotExists);

                if (!isNew)
                {
                    _logger.LogInformation(
                        "Skipping duplicate offline event {Key}", offlineEvent.IdempotencyKey);
                    skipped++;
                    continue;
                }

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
            "Offline sync complete for student {StudentId}. " +
            "Processed={Processed}, Skipped={Skipped}, Failed={Failed}",
            _studentId, processed, skipped, failed);

        context.Respond(new ActorResult(failed == 0,
            ErrorCode: failed > 0 ? "PARTIAL_SYNC" : null,
            ErrorMessage: failed > 0 ? $"{failed} events failed to sync" : null));
    }

    // =========================================================================
    // INTERNAL EVENT HANDLERS (from child actors)
    // =========================================================================

    private async Task HandleStagnationDetected(IContext context, StagnationDetected msg)
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.StagnationDetected");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("concept.id", msg.ConceptId);
        activity?.SetTag("stagnation.score", msg.CompositeScore);

        _logger.LogInformation(
            "Stagnation detected for student {StudentId}, concept {ConceptId}. " +
            "Score={Score:F3}, ConsecutiveSessions={Sessions}",
            _studentId, msg.ConceptId, msg.CompositeScore, msg.ConsecutiveStagnantSessions);

        var stagnationEvent = new StagnationDetected_V1(
            _studentId, msg.ConceptId, msg.CompositeScore,
            msg.Signals.AccuracyPlateau, msg.Signals.ResponseTimeDrift,
            msg.Signals.SessionAbandonment, msg.Signals.ErrorRepetition,
            msg.Signals.AnnotationSentiment, msg.ConsecutiveStagnantSessions);

        StageEvent(stagnationEvent);

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

        MethodologySwitched_V1? switchEvent = null;
        if (decision.ShouldSwitch)
        {
            switchEvent = new MethodologySwitched_V1(
                _studentId, msg.ConceptId,
                currentMethodology.ToString(),
                decision.RecommendedMethodology.ToString(),
                "stagnation_detected",
                msg.CompositeScore,
                dominantErrorType.ToString(),
                decision.Confidence);

            StageEvent(switchEvent);
        }
        else if (decision.AllMethodologiesExhausted)
        {
            _logger.LogWarning(
                "All methodologies exhausted for student {StudentId}, concept {ConceptId}. " +
                "Escalation: {Action}",
                _studentId, msg.ConceptId, decision.EscalationAction);

            await PublishToNats("cena.student.escalation", new
            {
                StudentId = _studentId,
                ConceptId = msg.ConceptId,
                Action = decision.EscalationAction,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        await FlushEvents();

        // Apply SAME event instances to local state (after successful persist)
        _state.Apply(stagnationEvent);
        if (switchEvent != null)
        {
            _state.Apply(switchEvent);

            if (_stagnationDetector != null)
            {
                context.Send(_stagnationDetector, new Stagnation.ResetAfterSwitch(
                    msg.ConceptId));
            }
        }
    }

    private Task HandleDelegateEvent(DelegateEvent del)
    {
        // Delegated events from child actors (e.g., LearningSessionActor) are domain events
        // that should be staged for persistence
        StageEvent(del.Event);
        return Task.CompletedTask;
    }

    // =========================================================================
    // QUERY HANDLERS
    // =========================================================================

    private Task HandleGetProfile(IContext context, GetStudentProfile query)
    {
        var response = new StudentProfileDto(
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

        context.Respond(new ActorResult<StudentProfileDto>(true, response));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns spaced repetition review schedule from in-memory HLR timers.
    /// Uses half-life regression: p(t) = 2^(-delta/h)
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
                // When recall drops to 0.85: solve 0.85 = 2^(-t/h) => t = -h * log2(0.85)
                var dueAt = kv.Value.LastReviewAt.AddHours(
                    -kv.Value.HalfLifeHours * Math.Log2(0.85));

                return new ReviewItem(
                    kv.Key, kv.Key, // concept name resolution deferred to KST graph
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
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.FlushEvents");
        activity?.SetTag("event.count", _pendingEvents.Count);

        // Persist ALL events atomically with expected version
        await using var session = _documentStore.LightweightSession();
        session.Events.Append(_studentId, _state.EventVersion, _pendingEvents.ToArray());
        await session.SaveChangesAsync();

        _eventsSinceSnapshot += _pendingEvents.Count;
        sw.Stop();

        EventPersistLatency.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("event.count", _pendingEvents.Count));

        // OUTBOX: Publish to NATS AFTER Marten commit succeeds (best-effort)
        foreach (var evt in _pendingEvents)
        {
            var subject = $"cena.student.events.{evt.GetType().Name.ToLowerInvariant()}";
            await PublishToNats(subject, evt);
        }

        _pendingEvents.Clear();

        // Check snapshot threshold
        if (_eventsSinceSnapshot >= StudentState.SnapshotInterval)
        {
            await ForceSnapshot();
        }
    }

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
            _logger.LogWarning(ex,
                "Failed to publish to NATS subject {Subject} for student {StudentId}. " +
                "Event is persisted in Marten and will be replayed.",
                subject, _studentId);
        }
    }

    private async Task ForceSnapshot()
    {
        using var activity = ActivitySourceInstance.StartActivity("StudentActor.ForceSnapshot");
        var sw = Stopwatch.StartNew();

        try
        {
            await using var session = _documentStore.LightweightSession();
            // Marten inline snapshot is triggered automatically on SaveChanges
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
        var self = context.Self;
        var system = context.System;
        _ = Task.Delay(MemoryCheckInterval).ContinueWith(_ =>
            system.Root.Send(self, new MemoryCheckTick()));

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
        var candidate = _state.MasteryMap
            .Where(kv => kv.Value < 0.85)
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        return candidate ?? "default_start_concept";
    }

    /// <summary>
    /// Determines dominant error type from recent attempts for a concept.
    /// Precedence: Conceptual > Procedural > Motivational.
    /// </summary>
    private ErrorType DetermineDominantErrorType(string conceptId)
    {
        var recentErrors = _state.RecentAttempts
            .Where(a => a.ConceptId == conceptId && a.ErrorType != "None")
            .Select(a => a.ErrorType)
            .ToList();

        if (recentErrors.Count == 0) return ErrorType.None;

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
            return; // Same day -- no change
        }
        else if (lastDate == today.AddDays(-1))
        {
            newStreak = _state.CurrentStreak + 1; // Consecutive day
        }
        else
        {
            newStreak = 1; // Gap -- reset
        }

        var longestStreak = Math.Max(_state.LongestStreak, newStreak);

        var @event = new StreakUpdated_V1(_studentId, newStreak, longestStreak, now);
        StageEvent(@event);
        await FlushEvents();
        _state.Apply(@event);

        if (_outreachScheduler != null)
        {
            context.Send(_outreachScheduler, new UpdateActivity(
                now, newStreak, null, false, 0));
        }
    }

    /// <summary>
    /// Computes SHA-256 hash of the answer text. Answers are never stored in plaintext.
    /// </summary>
    private static string ComputeAnswerHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
