// =============================================================================
// Cena Platform -- StudentActor.Queries (Partial)
// Extracted: Internal event handlers, query handlers, memory management
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

using Proto;
using Proto.Cluster;
using StackExchange.Redis;

namespace Cena.Actors.Students;

public sealed partial class StudentActor
{
    // =========================================================================
    // INTERNAL EVENT HANDLERS (from child actors)
    // =========================================================================

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
                decision.Confidence,
                DateTimeOffset.UtcNow);

            StageEvent(switchEvent);
        }
        else if (decision.AllMethodologiesExhausted)
        {
            _logger.LogWarning(
                "All methodologies exhausted for student {StudentId}, concept {ConceptId}. " +
                "Escalation: {Action}",
                _studentId, msg.ConceptId, decision.EscalationAction);

            // ACT-027: Fire-and-forget escalation notification (not a domain event).
            // Published directly since this is an operational alert, not persisted in event stream.
            try
            {
                await _nats.PublishAsync("cena.student.escalation",
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        StudentId = _studentId,
                        ConceptId = msg.ConceptId,
                        Action = decision.EscalationAction,
                        Timestamp = DateTimeOffset.UtcNow
                    }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish escalation for student {StudentId}", _studentId);
            }
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

    // ACT-029: Flush immediately after staging to prevent data loss on passivation.
    // Apply the event to local state after successful persistence.
    private async Task HandleDelegateEvent(DelegateEvent del)
    {
        StageEvent(del.Event);
        await FlushEvents();

        // Apply to local state based on event type
        switch (del.Event)
        {
            case ConceptAttempted_V1 e: _state.Apply(e); break;
            case ConceptMastered_V1 e: _state.Apply(e); break;
            case SessionStarted_V1 e: _state.Apply(e); break;
            case SessionEnded_V1 e: _state.Apply(e); break;
            case MethodologySwitched_V1 e: _state.Apply(e); break;
            case XpAwarded_V1 e: _state.Apply(e); break;
            case StreakUpdated_V1 e: _state.Apply(e); break;
            case AnnotationAdded_V1 e: _state.Apply(e); break;
            case StagnationDetected_V1 e: _state.Apply(e); break;
            case HintRequested_V1: break; // No state mutation
            case QuestionSkipped_V1: break; // No state mutation
            default:
                _logger.LogWarning("Unhandled delegate event type: {Type}", del.Event.GetType().Name);
                break;
        }
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
    // MEMORY MANAGEMENT
    // =========================================================================

    private Task HandleMemoryCheck(IContext context)
    {
        var estimated = _state.EstimateMemoryBytes();
        _actorMemoryUsage.Record(estimated,
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
}
