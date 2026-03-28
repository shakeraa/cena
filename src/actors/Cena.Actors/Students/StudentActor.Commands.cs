// =============================================================================
// Cena Platform -- StudentActor.Commands (Partial: Command Handlers)
// Extracted from StudentActor.cs for file-size governance (<500 lines each).
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Events;
using Cena.Actors.Hints;
using Cena.Actors.Infrastructure;
using Cena.Actors.Outreach;
using Cena.Actors.Services;
using Cena.Actors.Sessions;
using Cena.Actors.Stagnation;
using ErrorType = Cena.Actors.Mastery.ErrorType;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

using Proto;
using Proto.Cluster;
using StackExchange.Redis;

namespace Cena.Actors.Students;

public sealed partial class StudentActor
{
    // =========================================================================
    // COMMAND HANDLERS
    // =========================================================================

    /// <summary>
    /// Handles a concept attempt -- the primary hot-path command.
    /// Validates input, runs BKT via session actor, stages events, flushes atomically.
    /// </summary>
    private async Task HandleAttemptConcept(IContext context, AttemptConcept cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.AttemptConcept");
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
                // Session actor not yet available -- perform inline BKT via IBktService
                // ACT-019 FIX: Use BktParameters.Default instead of hardcoded constants
                // to prevent mastery divergence between inline and session-actor paths.
                var priorMastery = _state.MasteryMap.GetValueOrDefault(cmd.ConceptId, 0.3);
                bool isCorrect = !cmd.WasSkipped; // conservative assumption without evaluator

                // SAI-002: Use hint-adjusted BKT when hints were used.
                // Credit curve: 0 hints = 1.0x, 1 = 0.7x, 2 = 0.4x, 3+ = 0.1x.
                var bktInput = new BktUpdateInput(
                    PriorMastery: priorMastery,
                    IsCorrect: isCorrect,
                    Parameters: BktParameters.Default);

                var bktResult = cmd.HintCountUsed > 0
                    ? _hintAdjustedBktService.UpdateWithHints(bktInput, cmd.HintCountUsed)
                    : _bktService.Update(bktInput);

                double updatedMastery = bktResult.PosteriorMastery;

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

                // ---- Check mastery threshold ----
                ConceptMastered_V1? masteredEvent = null;
                if (updatedMastery >= MasteryConstants.ProgressionThreshold && priorMastery < MasteryConstants.ProgressionThreshold)
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

                // MST-006: Mastery pipeline enrichment (HLR, quality, stagnation)
                EnrichMasteryAfterAttempt(cmd.ConceptId, isCorrect, cmd.ResponseTimeMs,
                    ErrorType.None.ToString(), DateTimeOffset.UtcNow);

                // ACT-020: Accumulate session-level stagnation signals
                AccumulateSessionSignals(cmd.ConceptId, isCorrect, cmd.ResponseTimeMs, ErrorType.None);

                // ---- Telemetry ----
                _attemptCounter.Add(1,
                    new KeyValuePair<string, object?>("student.id", _studentId),
                    new KeyValuePair<string, object?>("correct", isCorrect));

                // SAI-003: Resolve explanation via L2 cache → L1 static → L3 LLM pipeline
                string explanation = "";
                try
                {
                    await using var qs = _documentStore.QuerySession();
                    var readModel = await qs.LoadAsync<Questions.QuestionReadModel>(cmd.QuestionId);

                    if (!isCorrect && readModel != null)
                    {
                        explanation = await ResolveExplanationAsync(
                            readModel, cmd.Answer, ErrorType.None.ToString(),
                            GetActiveMethodology(cmd.ConceptId),
                            priorMastery, CancellationToken.None,
                            conceptId: cmd.ConceptId,
                            backspaceCount: cmd.BackspaceCount,
                            answerChangeCount: cmd.AnswerChangeCount,
                            responseTimeMs: cmd.ResponseTimeMs);
                    }
                    else
                    {
                        explanation = readModel?.Explanation ?? "";
                    }
                }
                catch (Exception expl)
                {
                    _logger.LogWarning(expl,
                        "Failed to resolve explanation for question {QuestionId}", cmd.QuestionId);
                }

                // SAI-005: Gate explanation delivery via DeliveryGate
                // Explanations are system-initiated (IsStudentInitiated = false).
                // ConfusionDetector needs signal tracking that only the session actor has,
                // so for the inline fallback path we use NotConfused (conservative default).
                var explGateDecision = _deliveryGate.Evaluate(new DeliveryContext(
                    ConfusionState: ConfusionState.NotConfused,
                    DisengagementType: null,
                    FocusLevel: FocusLevel.Engaged,
                    IsStudentInitiated: false,
                    QuestionsUntilPatience: 5));

                if (explGateDecision.Action != DeliveryAction.Deliver)
                {
                    explanation = "";
                    _logger.LogDebug("Inline path: explanation {Action} — {Reason}",
                        explGateDecision.Action, explGateDecision.Reason);
                }

                var evalResponse = new EvaluateAnswerResponse(
                    cmd.QuestionId, isCorrect, isCorrect ? 1.0 : 0.0,
                    explanation,
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
            if (eval.UpdatedMastery >= MasteryConstants.ProgressionThreshold && prior < MasteryConstants.ProgressionThreshold)
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

            // MST-006: Mastery pipeline enrichment (HLR, quality, stagnation)
            EnrichMasteryAfterAttempt(cmd.ConceptId, eval.IsCorrect, cmd.ResponseTimeMs,
                eval.ClassifiedErrorType.ToString(), DateTimeOffset.UtcNow);

            // ACT-020: Accumulate session-level stagnation signals
            AccumulateSessionSignals(cmd.ConceptId, eval.IsCorrect, cmd.ResponseTimeMs, eval.ClassifiedErrorType);

            _attemptCounter.Add(1,
                new KeyValuePair<string, object?>("student.id", _studentId),
                new KeyValuePair<string, object?>("correct", eval.IsCorrect));

            // SAI-003: Enrich explanation via L2 cache → L1 static → L3 LLM pipeline
            var enrichedEval = eval;
            if (!eval.IsCorrect && string.IsNullOrWhiteSpace(eval.Explanation))
            {
                try
                {
                    await using var qs = _documentStore.QuerySession();
                    var readModel = await qs.LoadAsync<Questions.QuestionReadModel>(cmd.QuestionId);

                    if (readModel != null)
                    {
                        var explanation = await ResolveExplanationAsync(
                            readModel, cmd.Answer, eval.ClassifiedErrorType.ToString(),
                            GetActiveMethodology(cmd.ConceptId),
                            prior, CancellationToken.None,
                            conceptId: cmd.ConceptId,
                            backspaceCount: cmd.BackspaceCount,
                            answerChangeCount: cmd.AnswerChangeCount,
                            responseTimeMs: cmd.ResponseTimeMs);

                        enrichedEval = eval with { Explanation = explanation };
                    }
                }
                catch (Exception expl)
                {
                    _logger.LogWarning(expl,
                        "SAI-003: Failed to enrich explanation for question {QuestionId}", cmd.QuestionId);
                }
            }

            context.Respond(new ActorResult<EvaluateAnswerResponse>(true, enrichedEval));
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
        using var activity = _activitySource.StartActivity("StudentActor.StartSession");
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

            // ACT-020: Reset session-level stagnation accumulators
            ResetSessionAccumulators();

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

            // ACT-020: Send accumulated session signals to stagnation detector, then check
            var stagnationConceptId = _sessionPrimaryConceptId ?? summary?.LastConceptId;
            if (_stagnationDetector != null && stagnationConceptId != null && _sessionAttemptCount > 0)
            {
                double sessionAccuracy = _sessionAttemptCount > 0
                    ? (double)_sessionCorrectCount / _sessionAttemptCount
                    : 0;
                double avgRt = _sessionAttemptCount > 0
                    ? _sessionTotalRtMs / _sessionAttemptCount
                    : 0;
                double durationMinutes = summary?.DurationMinutes ?? 0;
                int errorRepeatCount = _sessionErrorCounts.Values.Where(c => c > 1).Sum();
                double annotationSentiment = _sessionAnnotationCount > 0
                    ? _sessionAnnotationSentimentSum / _sessionAnnotationCount
                    : 0.5;

                context.Send(_stagnationDetector, new Stagnation.UpdateStagnationSignals(
                    stagnationConceptId, sessionAccuracy, avgRt,
                    durationMinutes, errorRepeatCount, annotationSentiment));

                context.Send(_stagnationDetector, new Stagnation.CheckStagnation(
                    stagnationConceptId));
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
        using var activity = _activitySource.StartActivity("StudentActor.SwitchMethodology");
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
                decision.Confidence,
                DateTimeOffset.UtcNow);

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
        using var activity = _activitySource.StartActivity("StudentActor.AddAnnotation");

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

            // ACT-020: Accumulate annotation sentiment for session-level stagnation signals
            _sessionAnnotationSentimentSum += sentimentScore;
            _sessionAnnotationCount++;

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
    /// ACT-022: Delegates to OfflineSyncHandler for three-tier classification
    /// (Unconditional, Conditional, ServerAuthoritative) with weight-based acceptance.
    /// </summary>
    private async Task HandleSyncOfflineEvents(IContext context, SyncOfflineEvents cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.SyncOfflineEvents");
        activity?.SetTag("student.id", _studentId);
        activity?.SetTag("event.count", cmd.Events.Count);

        try
        {
            var (syncResult, domainEvents) = await _offlineSyncHandler.ProcessAsync(cmd, _state);

            // Stage returned domain events for atomic persistence
            foreach (var domainEvent in domainEvents)
                StageEvent(domainEvent);

            if (domainEvents.Count > 0)
            {
                await FlushEvents();

                // Apply events to local state
                foreach (var domainEvent in domainEvents)
                {
                    if (domainEvent is Events.ConceptAttempted_V1 attempt)
                        _state.Apply(attempt);
                }
            }

            bool success = syncResult.Rejected == 0;
            context.Respond(new ActorResult(success,
                ErrorCode: !success ? "PARTIAL_SYNC" : null,
                ErrorMessage: !success
                    ? $"{syncResult.Rejected} events rejected, {syncResult.Duplicates} duplicates"
                    : null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to sync offline events for student {StudentId}", _studentId);
            context.Respond(new ActorResult(
                false, ErrorCode: "SYNC_FAILED", ErrorMessage: ex.Message));
        }
    }

    // =========================================================================
    // CHILD ACTOR SPAWNING
    // =========================================================================

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
    // HELPER METHODS (used by command handlers)
    // =========================================================================

    private string GetActiveMethodology(string conceptId)
    {
        return _state.MethodologyMap.GetValueOrDefault(conceptId, Methodology.Socratic).ToString();
    }

    private string SelectNextConcept()
    {
        var candidate = _state.MasteryMap
            .Where(kv => kv.Value < MasteryConstants.ProgressionThreshold)
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        return candidate ?? "default_start_concept";
    }

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

    private static int CalculateXpAward(bool isCorrect, int hintsUsed, double score)
    {
        if (!isCorrect) return (int)(score * 5);
        return Math.Max(2, 10 - (hintsUsed * 2));
    }

    private async Task UpdateStreak(IContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var lastDate = _state.LastActivityDate.Date;
        var today = now.Date;

        int newStreak;
        if (lastDate == today)
            return;
        else if (lastDate == today.AddDays(-1))
            newStreak = _state.CurrentStreak + 1;
        else
            newStreak = 1;

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

    private static string ComputeAnswerHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // =========================================================================
    // ACT-020: Session-level stagnation signal accumulation
    // =========================================================================

    private void AccumulateSessionSignals(string conceptId, bool isCorrect, int responseTimeMs, ErrorType errorType)
    {
        _sessionAttemptCount++;
        if (isCorrect) _sessionCorrectCount++;
        _sessionTotalRtMs += responseTimeMs;

        // Track primary concept (most-attempted this session)
        _sessionPrimaryConceptId ??= conceptId;

        // Track error types for repetition count
        if (errorType != ErrorType.None)
        {
            var key = errorType.ToString();
            _sessionErrorCounts[key] = _sessionErrorCounts.GetValueOrDefault(key, 0) + 1;
        }
    }

    private void ResetSessionAccumulators()
    {
        _sessionAttemptCount = 0;
        _sessionCorrectCount = 0;
        _sessionTotalRtMs = 0;
        _sessionErrorCounts.Clear();
        _sessionPrimaryConceptId = null;
        _sessionAnnotationSentimentSum = 0;
        _sessionAnnotationCount = 0;
    }

    // =========================================================================
    // SAI-003: Explanation resolution via L2 cache → L1 static → L3 LLM
    // =========================================================================

    /// <summary>
    /// Resolves the best explanation using the ExplanationOrchestrator pipeline.
    /// Applies rate limiting: max 3 LLM generation calls per student per minute.
    /// If rate limit is hit, falls back to L1 static or empty string.
    /// SAI-004: Builds L3 context from available student signals for personalized explanations.
    /// </summary>
    private async Task<string> ResolveExplanationAsync(
        Questions.QuestionReadModel readModel,
        string studentAnswer,
        string errorType,
        string methodology,
        double conceptMastery,
        CancellationToken ct,
        string? conceptId = null,
        int backspaceCount = 0,
        int answerChangeCount = 0,
        int responseTimeMs = 0)
    {
        // Rate limit check: prune timestamps older than 1 minute
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        while (_llmExplanationTimestamps.Count > 0 && _llmExplanationTimestamps.Peek() < cutoff)
            _llmExplanationTimestamps.Dequeue();

        // SAI-004: Build L3 context from available student signals
        L3ExplanationRequest? l3Context = null;
        if (conceptId is not null)
        {
            l3Context = BuildL3Context(
                readModel, studentAnswer, errorType, methodology,
                conceptMastery, conceptId,
                backspaceCount, answerChangeCount, responseTimeMs);
        }

        var request = new ExplanationRequest(
            QuestionId: readModel.Id,
            StaticExplanation: readModel.Explanation,
            QuestionStem: readModel.StemPreview,
            CorrectAnswer: "", // QuestionReadModel doesn't carry full options
            StudentAnswer: studentAnswer,
            ErrorType: errorType,
            Methodology: methodology,
            DistractorRationale: null,
            BloomsLevel: readModel.BloomsLevel,
            Subject: readModel.Subject,
            Language: readModel.Language,
            ConceptMastery: conceptMastery,
            QuestionDifficulty: readModel.Difficulty,
            L3Context: l3Context);

        // If rate limit exceeded, the orchestrator will still check L2 cache and L1 static.
        // Only L3 LLM generation needs gating. The orchestrator's try-catch on LLM failure
        // naturally falls back, but we should skip if we know we're rate-limited.
        if (_llmExplanationTimestamps.Count >= MaxLlmExplanationsPerMinute)
        {
            _logger.LogDebug(
                "SAI-003: LLM rate limit ({Max}/min) reached for student {StudentId}. " +
                "Using cache/static only.",
                MaxLlmExplanationsPerMinute, _studentId);

            // Try L2 cache only (orchestrator still checks cache first, but this avoids
            // the LLM attempt entirely by returning static fallback)
            return readModel.Explanation ?? "";
        }

        var explanation = await _explanationOrchestrator.ResolveAsync(request, ct);

        // Track the LLM call timestamp (the orchestrator may or may not have hit L3,
        // but we count each resolution attempt toward the rate limit for safety)
        _llmExplanationTimestamps.Enqueue(DateTimeOffset.UtcNow);

        return explanation;
    }

    // =========================================================================
    // SAI-004: L3 Context Builder
    // =========================================================================

    /// <summary>
    /// Builds L3ExplanationRequest from available student state signals.
    /// Collects mastery, affect, behavioral, and instructional context.
    /// Never includes student ID or PII.
    /// </summary>
    private L3ExplanationRequest? BuildL3Context(
        Questions.QuestionReadModel readModel,
        string studentAnswer,
        string errorType,
        string methodology,
        double conceptMastery,
        string conceptId,
        int backspaceCount,
        int answerChangeCount,
        int responseTimeMs)
    {
        try
        {
            // ── Mastery context ──
            var masteryState = _state.MasteryOverlay.GetValueOrDefault(conceptId);
            var now = DateTimeOffset.UtcNow;
            double recallProbability = masteryState?.RecallProbability(now) ?? 0.0;
            int bloomLevel = masteryState?.BloomLevel ?? readModel.BloomsLevel;
            var recentErrors = masteryState?.RecentErrors
                .Select(e => e.ToString())
                .ToList() as IReadOnlyList<string> ?? Array.Empty<string>();
            var qualityQuadrant = masteryState?.QualityQuadrant ?? Cena.Actors.Mastery.MasteryQuality.Struggling;
            var methodHistory = _state.MethodAttemptHistory
                .GetValueOrDefault(conceptId, new())
                .Select(m => m.Methodology)
                .ToList() as IReadOnlyList<string> ?? Array.Empty<string>();

            // ── Scaffolding ──
            float effectiveMastery = (float)conceptMastery;
            // PSI: use 1.0 (always ready) as default when no graph cache is available
            float psi = 1.0f;
            var scaffoldingLevel = Cena.Actors.Mastery.ScaffoldingService.DetermineLevel(effectiveMastery, psi);

            // ── Affect context (SAI-004: use real detectors instead of hardcoded defaults) ──
            double medianRtMs = _state.ResponseBaseline.MedianResponseTimeMs;
            double rtRatio = medianRtMs > 0 ? responseTimeMs / medianRtMs : 1.0;
            bool wrongOnMastered = conceptMastery >= 0.7;
            var recentAttempts = _state.RecentAttempts;
            double recentAccuracy = recentAttempts.Count >= 5
                ? recentAttempts.TakeLast(5).Count(a => a.IsCorrect) / 5.0
                : recentAttempts.Count > 0
                    ? recentAttempts.Count(a => a.IsCorrect) / (double)recentAttempts.Count
                    : 0.5;

            var confusionState = _confusionDetector.Detect(new ConfusionInput(
                WrongOnMasteredConcept: wrongOnMastered,
                ResponseTimeRatio: rtRatio,
                LastAnswerCorrect: recentAttempts.Count > 0 && recentAttempts[^1].IsCorrect,
                AnswerChangedCount: answerChangeCount,
                HintRequestedThenCancelled: false,
                QuestionsInConfusionWindow: 0,
                AccuracyInConfusionWindow: recentAccuracy));

            var disengagementType = _disengagementClassifier.Classify(new DisengagementInput(
                RecentAccuracy: recentAccuracy,
                ResponseTimeRatio: rtRatio,
                EngagementTrend: 0.0,
                HintRequestRate: 0.0,
                AppBackgroundingRate: 0.0,
                MinutesSinceLastBreak: 10,
                TouchPatternConsistencyDelta: 0.0,
                SessionsToday: 1,
                MinutesInSession: 10,
                IsLateEvening: false));

            var focusLevel = disengagementType switch
            {
                Services.DisengagementType.Fatigued_Cognitive
                    or Services.DisengagementType.Fatigued_Motor => FocusLevel.Fatigued,
                Services.DisengagementType.Bored_TooEasy
                    or Services.DisengagementType.Bored_NoValue => FocusLevel.DisengagedBored,
                Services.DisengagementType.Mixed => FocusLevel.Drifting,
                _ => FocusLevel.Engaged
            };

            return new L3ExplanationRequest
            {
                QuestionId = readModel.Id,
                QuestionStem = readModel.StemPreview,
                CorrectAnswer = "",
                StudentAnswer = studentAnswer,
                ErrorType = errorType,
                Subject = readModel.Subject,
                Language = readModel.Language,
                StaticExplanation = readModel.Explanation,
                DistractorRationale = null,
                MasteryProbability = conceptMastery,
                RecallProbability = recallProbability,
                BloomLevel = bloomLevel,
                Psi = psi,
                RecentErrorTypes = recentErrors,
                QualityQuadrant = qualityQuadrant,
                ScaffoldingLevel = scaffoldingLevel,
                Methodology = methodology,
                MethodHistory = methodHistory,
                FocusLevel = focusLevel,
                DisengagementType = disengagementType,
                ConfusionState = confusionState,
                BackspaceCount = backspaceCount,
                AnswerChangeCount = answerChangeCount,
                ResponseTimeMs = responseTimeMs,
                MedianResponseTimeMs = medianRtMs,
                QuestionDifficulty = readModel.Difficulty
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SAI-004: Failed to build L3 context for concept {ConceptId}. " +
                "Falling back to L2-only resolution.",
                conceptId);
            return null;
        }
    }
}
