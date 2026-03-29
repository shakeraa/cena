// =============================================================================
// Cena Platform -- StudentActor.Methodology (Partial)
// Hierarchical methodology: resolution, confidence tracking, teacher overrides,
// cooldown enforcement, and NATS admin alerts.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.MethodologyHierarchy;
using Cena.Actors.Services;
using Microsoft.Extensions.Logging;
using Proto;
using ErrorType = Cena.Actors.Mastery.ErrorType;

namespace Cena.Actors.Students;

public sealed partial class StudentActor
{
    // =========================================================================
    // METHODOLOGY HIERARCHY — called after each attempt to update all levels
    // =========================================================================

    /// <summary>
    /// Update the hierarchical methodology maps after a concept attempt.
    /// Checks for confidence threshold crossings and emits events when reached.
    /// </summary>
    private void UpdateMethodologyHierarchyAfterAttempt(
        string conceptId, string? topicId, string? subjectId,
        bool isCorrect, string methodologyActive)
    {
        // Track attempt counts at prior state for threshold crossing detection
        int priorConceptN = _state.ConceptMethodologyMap.TryGetValue(conceptId, out var ca) ? ca.AttemptCount : 0;
        int priorTopicN = topicId != null && _state.TopicMethodologyMap.TryGetValue(topicId, out var ta) ? ta.AttemptCount : 0;
        int priorSubjectN = subjectId != null && _state.SubjectMethodologyMap.TryGetValue(subjectId, out var sa) ? sa.AttemptCount : 0;

        // Update all levels
        _state.UpdateMethodologyHierarchy(conceptId, topicId, subjectId, isCorrect, methodologyActive);

        // Check for confidence threshold crossings
        CheckConfidenceThreshold(conceptId, MethodologyLevel.Concept, priorConceptN,
            MethodologyResolver.ConceptConfidenceThreshold, _state.ConceptMethodologyMap);

        if (topicId != null)
            CheckConfidenceThreshold(topicId, MethodologyLevel.Topic, priorTopicN,
                MethodologyResolver.TopicConfidenceThreshold, _state.TopicMethodologyMap);

        if (subjectId != null)
            CheckConfidenceThreshold(subjectId, MethodologyLevel.Subject, priorSubjectN,
                MethodologyResolver.SubjectConfidenceThreshold, _state.SubjectMethodologyMap);
    }

    /// <summary>
    /// Detect when a level crosses the confidence threshold for the first time.
    /// Stages a MethodologyConfidenceReached_V1 event and publishes a NATS admin alert.
    /// </summary>
    private void CheckConfidenceThreshold(
        string levelId,
        MethodologyLevel level,
        int priorAttemptCount,
        int threshold,
        Dictionary<string, MethodologyAssignment> map)
    {
        if (!map.TryGetValue(levelId, out var assignment)) return;

        // Check if we just crossed the threshold
        if (priorAttemptCount < threshold && assignment.AttemptCount >= threshold
            && assignment.ConfidenceReachedAt == null)
        {
            var now = DateTimeOffset.UtcNow;

            // Mark confidence reached
            map[levelId] = assignment with
            {
                ConfidenceReachedAt = now,
                Source = MethodologySource.DataDriven
            };

            var @event = new MethodologyConfidenceReached_V1(
                _studentId, level.ToString(), levelId,
                assignment.Methodology.ToString(),
                assignment.Confidence, assignment.AttemptCount,
                assignment.SuccessRate, now);

            StageEvent(@event);

            _logger.LogInformation(
                "Methodology confidence reached for student {StudentId} at {Level}/{LevelId}: " +
                "{Methodology} (N={N}, confidence={Confidence:F2}, success={Success:P0})",
                _studentId, level, levelId, assignment.Methodology,
                assignment.AttemptCount, assignment.Confidence, assignment.SuccessRate);

            // Fire-and-forget NATS admin alert with explicit error handling
            _ = PublishMethodologyAlert(level, levelId, assignment)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.LogError(t.Exception, "Failed to publish methodology alert for {Level}", level);
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private async Task PublishMethodologyAlert(
        MethodologyLevel level, string levelId, MethodologyAssignment assignment)
    {
        await _nats.PublishAsync("cena.admin.methodology.confidence-reached",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                StudentId = _studentId,
                Level = level.ToString(),
                LevelId = levelId,
                Methodology = assignment.Methodology.ToString(),
                Confidence = assignment.Confidence,
                AttemptCount = assignment.AttemptCount,
                SuccessRate = assignment.SuccessRate,
                Timestamp = DateTimeOffset.UtcNow
            }));
    }

    // =========================================================================
    // RESOLVE METHODOLOGY — replaces flat MethodologyMap lookup
    // =========================================================================

    /// <summary>
    /// Resolve the effective methodology for a concept through the hierarchy.
    /// Used by command handlers instead of direct MethodologyMap lookup.
    /// </summary>
    private MethodologyResolution ResolveMethodologyForConcept(string conceptId, string? topicId = null, string? subjectId = null)
    {
        return MethodologyResolver.Resolve(
            conceptId, topicId, subjectId,
            _state.ConceptMethodologyMap,
            _state.TopicMethodologyMap,
            _state.SubjectMethodologyMap,
            _state.MethodologyMap);
    }

    // =========================================================================
    // STAGNATION HANDLER — enhanced with cooldown and deferred switch events
    // =========================================================================

    /// <summary>
    /// Enhanced stagnation handler that enforces cooldown and emits deferred events.
    /// </summary>
    private async Task HandleStagnationWithHierarchy(
        IContext context, StagnationDetected msg, string? topicId, string? subjectId)
    {
        var stagnationEvent = new StagnationDetected_V1(
            _studentId, msg.ConceptId, msg.CompositeScore,
            msg.Signals.AccuracyPlateau, msg.Signals.ResponseTimeDrift,
            msg.Signals.SessionAbandonment, msg.Signals.ErrorRepetition,
            msg.Signals.AnnotationSentiment, msg.ConsecutiveStagnantSessions);

        StageEvent(stagnationEvent);

        var currentMethodology = _state.MethodologyMap.GetValueOrDefault(
            msg.ConceptId, Students.Methodology.Socratic);
        var dominantErrorType = DetermineDominantErrorType(msg.ConceptId);

        // Get current hierarchy assignment for cooldown check
        var currentAssignment = _state.ConceptMethodologyMap.GetValueOrDefault(msg.ConceptId);
        int sessionsSince = _state.SessionsSinceSwitch.GetValueOrDefault(msg.ConceptId, int.MaxValue);

        var decision = await _methodologySwitchService.DecideSwitch(
            new DecideSwitchRequest(
                _studentId, msg.ConceptId, msg.ConceptId,
                dominantErrorType, currentMethodology,
                _state.MethodAttemptHistory.GetValueOrDefault(msg.ConceptId, new())
                    .Select(m => m.Methodology).ToList(),
                msg.CompositeScore, msg.ConsecutiveStagnantSessions,
                currentAssignment, sessionsSince));

        if (decision.DeferredByCooldown)
        {
            // Emit deferred event for admin visibility
            var deferredEvent = new MethodologySwitchDeferred_V1(
                _studentId, msg.ConceptId,
                decision.RecommendedMethodology.ToString(),
                currentMethodology.ToString(),
                decision.DecisionTrace,
                decision.CooldownSessionsRemaining,
                decision.CooldownHoursRemaining,
                DateTimeOffset.UtcNow);

            StageEvent(deferredEvent);

            // Fire NATS alert so admin dashboard can show deferred switches
            try
            {
                await _nats.PublishAsync("cena.admin.methodology.switch-deferred",
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        StudentId = _studentId,
                        ConceptId = msg.ConceptId,
                        CurrentMethodology = currentMethodology.ToString(),
                        RecommendedMethodology = decision.RecommendedMethodology.ToString(),
                        Reason = decision.DecisionTrace,
                        CooldownRemaining = decision.CooldownSessionsRemaining,
                        Timestamp = DateTimeOffset.UtcNow
                    }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish switch deferred alert");
            }
        }
        else if (decision.ShouldSwitch)
        {
            var switchEvent = new MethodologySwitched_V1(
                _studentId, msg.ConceptId,
                currentMethodology.ToString(),
                decision.RecommendedMethodology.ToString(),
                "stagnation_detected",
                msg.CompositeScore,
                dominantErrorType.ToString(),
                decision.Confidence,
                DateTimeOffset.UtcNow);

            StageEvent(switchEvent);

            // Reset cooldown counter for this concept
            _state.SessionsSinceSwitch[msg.ConceptId] = 0;

            // Update hierarchy assignment
            if (_state.ConceptMethodologyMap.ContainsKey(msg.ConceptId))
            {
                _state.ConceptMethodologyMap[msg.ConceptId] =
                    _state.ConceptMethodologyMap[msg.ConceptId]
                        .WithSwitch(decision.RecommendedMethodology, MethodologySource.McmRouted, DateTimeOffset.UtcNow);
            }
        }
        else if (decision.AllMethodologiesExhausted)
        {
            _logger.LogWarning(
                "All methodologies exhausted for student {StudentId}, concept {ConceptId}. " +
                "Escalation: {Action}",
                _studentId, msg.ConceptId, decision.EscalationAction);

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

        _state.Apply(stagnationEvent);
        // MethodologySwitched is applied by the existing Apply method on FlushEvents
    }

    // =========================================================================
    // TEACHER OVERRIDE COMMAND
    // =========================================================================

    private async Task HandleTeacherMethodologyOverride(IContext context, TeacherMethodologyOverride cmd)
    {
        using var activity = _activitySource.StartActivity("StudentActor.TeacherOverride");

        try
        {
            var currentMethodology = cmd.Level switch
            {
                "Subject" => _state.SubjectMethodologyMap.GetValueOrDefault(cmd.LevelId)?.Methodology.ToString() ?? "None",
                "Topic" => _state.TopicMethodologyMap.GetValueOrDefault(cmd.LevelId)?.Methodology.ToString() ?? "None",
                _ => _state.MethodologyMap.GetValueOrDefault(cmd.LevelId, Students.Methodology.Socratic).ToString()
            };

            var @event = new TeacherMethodologyOverride_V1(
                _studentId, cmd.Level, cmd.LevelId,
                currentMethodology, cmd.Methodology,
                cmd.TeacherId, DateTimeOffset.UtcNow);

            StageEvent(@event);
            await FlushEvents();
            _state.Apply(@event);

            // Reset cooldown for overridden level
            _state.SessionsSinceSwitch[cmd.LevelId] = 0;

            context.Respond(new ActorResult(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed teacher methodology override for student {StudentId}", _studentId);
            context.Respond(new ActorResult(
                false, ErrorCode: "OVERRIDE_FAILED", ErrorMessage: ex.Message));
        }
    }

    // =========================================================================
    // METHODOLOGY PROFILE QUERY
    // =========================================================================

    private Task HandleGetMethodologyProfile(IContext context, GetMethodologyProfile query)
    {
        var conceptProfiles = new List<ConceptMethodologyProfileDto>();

        foreach (var (conceptId, assignment) in _state.ConceptMethodologyMap)
        {
            var resolution = ResolveMethodologyForConcept(conceptId);
            conceptProfiles.Add(new ConceptMethodologyProfileDto(
                conceptId,
                resolution.Assignment.Methodology.ToString(),
                resolution.ResolvedLevel.ToString(),
                resolution.Trace,
                assignment.AttemptCount,
                assignment.SuccessRate,
                assignment.Confidence,
                assignment.HasSufficientData(MethodologyResolver.ConceptConfidenceThreshold),
                assignment.Source.ToString()));
        }

        var topicProfiles = _state.TopicMethodologyMap
            .Select(kv => new TopicMethodologyProfileDto(
                kv.Key,
                kv.Value.Methodology.ToString(),
                kv.Value.AttemptCount,
                kv.Value.SuccessRate,
                kv.Value.Confidence,
                kv.Value.HasSufficientData(MethodologyResolver.TopicConfidenceThreshold),
                kv.Value.Source.ToString()))
            .ToList();

        var subjectProfiles = _state.SubjectMethodologyMap
            .Select(kv => new SubjectMethodologyProfileDto(
                kv.Key,
                kv.Value.Methodology.ToString(),
                kv.Value.AttemptCount,
                kv.Value.SuccessRate,
                kv.Value.Confidence,
                kv.Value.HasSufficientData(MethodologyResolver.SubjectConfidenceThreshold),
                kv.Value.Source.ToString()))
            .ToList();

        var response = new MethodologyProfileResponse(
            _studentId, subjectProfiles, topicProfiles, conceptProfiles);

        context.Respond(new ActorResult<MethodologyProfileResponse>(true, response));
        return Task.CompletedTask;
    }
}

// =============================================================================
// COMMANDS & QUERIES for methodology hierarchy
// =============================================================================

/// <summary>Teacher/admin override command.</summary>
public sealed record TeacherMethodologyOverride(
    string StudentId,
    string Level,       // "Subject", "Topic", "Concept"
    string LevelId,
    string Methodology,
    string TeacherId);

/// <summary>Query the full methodology profile for a student.</summary>
public sealed record GetMethodologyProfile(string StudentId);

// =============================================================================
// RESPONSE DTOs
// =============================================================================

public sealed record MethodologyProfileResponse(
    string StudentId,
    IReadOnlyList<SubjectMethodologyProfileDto> Subjects,
    IReadOnlyList<TopicMethodologyProfileDto> Topics,
    IReadOnlyList<ConceptMethodologyProfileDto> Concepts);

public sealed record SubjectMethodologyProfileDto(
    string SubjectId,
    string Methodology,
    int AttemptCount,
    float SuccessRate,
    float Confidence,
    bool HasSufficientData,
    string Source);

public sealed record TopicMethodologyProfileDto(
    string TopicId,
    string Methodology,
    int AttemptCount,
    float SuccessRate,
    float Confidence,
    bool HasSufficientData,
    string Source);

public sealed record ConceptMethodologyProfileDto(
    string ConceptId,
    string EffectiveMethodology,
    string ResolvedLevel,
    string ResolutionTrace,
    int AttemptCount,
    float SuccessRate,
    float Confidence,
    bool HasSufficientData,
    string Source);
