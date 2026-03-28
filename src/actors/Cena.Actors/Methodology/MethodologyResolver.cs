// =============================================================================
// Cena Platform -- MethodologyResolver (Domain Service)
// Hierarchical confidence-gated resolution: Concept → Topic → Subject → MCM → Blooms
// =============================================================================

using Cena.Actors.Students;

namespace Cena.Actors.Methodology;

/// <summary>
/// Resolves the active methodology for a concept by cascading through the hierarchy.
/// Each level is checked for sufficient data (confidence gate). The most specific level
/// with enough data wins. Everything else inherits from the parent.
/// </summary>
public static class MethodologyResolver
{
    /// <summary>Minimum attempts at concept level to override topic-level assignment.</summary>
    public const int ConceptConfidenceThreshold = 30;

    /// <summary>Minimum attempts at topic level to override subject-level assignment.</summary>
    public const int TopicConfidenceThreshold = 30;

    /// <summary>Minimum attempts at subject level for a data-driven assignment.</summary>
    public const int SubjectConfidenceThreshold = 50;

    /// <summary>Minimum sessions between methodology switches at any level.</summary>
    public const int CooldownSessions = 5;

    /// <summary>Minimum time between methodology switches.</summary>
    public static readonly TimeSpan CooldownDuration = TimeSpan.FromDays(7);

    /// <summary>
    /// Resolve the effective methodology for a concept, cascading through the hierarchy.
    /// Returns the assignment and the level it was resolved from.
    /// </summary>
    public static MethodologyResolution Resolve(
        string conceptId,
        string? topicId,
        string? subjectId,
        IReadOnlyDictionary<string, MethodologyAssignment> conceptMap,
        IReadOnlyDictionary<string, MethodologyAssignment> topicMap,
        IReadOnlyDictionary<string, MethodologyAssignment> subjectMap,
        IReadOnlyDictionary<string, Students.Methodology> mcmMap)
    {
        // Layer 5 → 3: Concept-level override (highest specificity)
        if (conceptMap.TryGetValue(conceptId, out var conceptAssignment))
        {
            if (conceptAssignment.Source == MethodologySource.TeacherOverride)
                return new(conceptAssignment, MethodologyLevel.Concept, "Teacher override at concept level");

            if (conceptAssignment.HasSufficientData(ConceptConfidenceThreshold))
                return new(conceptAssignment, MethodologyLevel.Concept,
                    $"Concept-level data-driven (N={conceptAssignment.AttemptCount}, confidence={conceptAssignment.Confidence:F2})");
        }

        // Layer 4: Topic-level assignment
        if (topicId != null && topicMap.TryGetValue(topicId, out var topicAssignment))
        {
            if (topicAssignment.Source == MethodologySource.TeacherOverride)
                return new(topicAssignment, MethodologyLevel.Topic, "Teacher override at topic level");

            if (topicAssignment.HasSufficientData(TopicConfidenceThreshold))
                return new(topicAssignment, MethodologyLevel.Topic,
                    $"Topic-level data-driven (N={topicAssignment.AttemptCount}, confidence={topicAssignment.Confidence:F2})");
        }

        // Layer 3: Subject-level assignment
        if (subjectId != null && subjectMap.TryGetValue(subjectId, out var subjectAssignment))
        {
            if (subjectAssignment.Source == MethodologySource.TeacherOverride)
                return new(subjectAssignment, MethodologyLevel.Subject, "Teacher override at subject level");

            if (subjectAssignment.HasSufficientData(SubjectConfidenceThreshold))
                return new(subjectAssignment, MethodologyLevel.Subject,
                    $"Subject-level data-driven (N={subjectAssignment.AttemptCount}, confidence={subjectAssignment.Confidence:F2})");
        }

        // Layer 2: MCM error-type routing (existing concept-level assignment)
        if (mcmMap.TryGetValue(conceptId, out var mcmMethodology))
        {
            var mcmAssignment = MethodologyAssignment.Default(mcmMethodology, MethodologySource.McmRouted);
            return new(mcmAssignment, MethodologyLevel.Concept, "MCM error-type routing");
        }

        // Layer 1: Blooms progression default
        var defaultAssignment = MethodologyAssignment.Default(
            Students.Methodology.Socratic, MethodologySource.BloomsDefault);
        return new(defaultAssignment, MethodologyLevel.Subject, "Blooms progression default");
    }

    /// <summary>
    /// Check if a methodology switch is allowed (cooldown not active).
    /// </summary>
    public static CooldownStatus CheckCooldown(
        MethodologyAssignment? currentAssignment,
        int sessionsSinceLastSwitch,
        DateTimeOffset now)
    {
        if (currentAssignment == null || currentAssignment.LastSwitchAt == DateTimeOffset.MinValue)
            return CooldownStatus.Allowed;

        bool sessionCooldown = sessionsSinceLastSwitch < CooldownSessions;
        bool timeCooldown = (now - currentAssignment.LastSwitchAt) < CooldownDuration;

        if (sessionCooldown || timeCooldown)
        {
            int sessionsRemaining = Math.Max(0, CooldownSessions - sessionsSinceLastSwitch);
            var timeRemaining = CooldownDuration - (now - currentAssignment.LastSwitchAt);
            if (timeRemaining < TimeSpan.Zero) timeRemaining = TimeSpan.Zero;

            return new CooldownStatus(
                IsAllowed: false,
                SessionsRemaining: sessionsRemaining,
                TimeRemaining: timeRemaining,
                Reason: $"Cooldown active: {sessionsRemaining} sessions or {timeRemaining.TotalHours:F0}h remaining");
        }

        return CooldownStatus.Allowed;
    }
}

/// <summary>
/// Result of resolving a methodology through the hierarchy.
/// </summary>
public sealed record MethodologyResolution(
    MethodologyAssignment Assignment,
    MethodologyLevel ResolvedLevel,
    string Trace);

/// <summary>
/// Result of cooldown check.
/// </summary>
public sealed record CooldownStatus(
    bool IsAllowed,
    int SessionsRemaining = 0,
    TimeSpan TimeRemaining = default,
    string? Reason = null)
{
    public static readonly CooldownStatus Allowed = new(true);
}
