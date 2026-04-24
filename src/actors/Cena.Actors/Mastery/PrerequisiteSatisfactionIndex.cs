// =============================================================================
// Cena Platform -- Prerequisite Satisfaction Index (PSI)
// MST-009: Average effective mastery of direct prerequisites
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// PSI quantifies how ready a student is for a concept based on prerequisite mastery.
/// PSI = average(mastery(p) for p in direct_prerequisites).
/// </summary>
public static class PrerequisiteSatisfactionIndex
{
    /// <summary>
    /// Compute PSI: average mastery of direct prerequisites.
    /// Returns 1.0 if concept has no prerequisites (always ready).
    /// </summary>
    public static float Compute(
        string conceptId,
        IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay,
        IConceptGraphCache graphCache)
    {
        var prerequisites = graphCache.GetPrerequisites(conceptId);
        if (prerequisites.Count == 0)
            return 1.0f;

        float sum = 0f;
        for (int i = 0; i < prerequisites.Count; i++)
        {
            string prereqId = prerequisites[i].SourceConceptId;
            float mastery = masteryOverlay.TryGetValue(prereqId, out var state)
                ? state.MasteryProbability
                : 0.0f;
            sum += mastery;
        }

        return Math.Clamp(sum / prerequisites.Count, 0.0f, 1.0f);
    }
}
