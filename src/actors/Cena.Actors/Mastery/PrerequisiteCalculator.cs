// =============================================================================
// Cena Platform -- Prerequisite Support Calculator
// MST-004: Computes prerequisite support for effective mastery
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Computes prerequisite support: how well the student has mastered
/// the foundational concepts required for a given concept.
/// All methods are static, zero allocation on hot path.
/// </summary>
public static class PrerequisiteCalculator
{
    private const float PrereqGateThreshold = MasteryConstants.ProgressionThresholdF;

    /// <summary>
    /// Primary formula: min(mastery(p) for p in prerequisites).
    /// Returns 1.0 if concept has no prerequisites.
    /// Missing prerequisite in overlay -> mastery = 0.0 (never encountered).
    /// </summary>
    public static float ComputeSupport(
        string conceptId,
        IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay,
        IConceptGraphCache graphCache)
    {
        var prerequisites = graphCache.GetPrerequisites(conceptId);
        if (prerequisites.Count == 0)
            return 1.0f;

        float min = float.MaxValue;
        for (int i = 0; i < prerequisites.Count; i++)
        {
            string prereqId = prerequisites[i].SourceConceptId;
            float mastery = masteryOverlay.TryGetValue(prereqId, out var state)
                ? state.MasteryProbability
                : 0.0f;

            if (mastery < min)
                min = mastery;
        }

        return Math.Clamp(min, 0.0f, 1.0f);
    }

    /// <summary>
    /// Weighted penalty fallback (Phase 1): product(max(mastery(p)/0.85, 1.0)).
    /// When all prerequisites >= 0.85, penalty is 1.0 (no reduction).
    /// Weak prerequisites compound multiplicatively.
    /// </summary>
    public static float ComputeWeightedPenalty(
        string conceptId,
        IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay,
        IConceptGraphCache graphCache)
    {
        var prerequisites = graphCache.GetPrerequisites(conceptId);
        if (prerequisites.Count == 0)
            return 1.0f;

        float penalty = 1.0f;
        for (int i = 0; i < prerequisites.Count; i++)
        {
            string prereqId = prerequisites[i].SourceConceptId;
            float mastery = masteryOverlay.TryGetValue(prereqId, out var state)
                ? state.MasteryProbability
                : 0.0f;

            float factor = mastery / PrereqGateThreshold;
            if (factor < 1.0f)
                penalty *= factor;
        }

        return Math.Clamp(penalty, 0.0f, 1.0f);
    }
}
