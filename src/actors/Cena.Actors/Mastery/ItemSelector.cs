// =============================================================================
// Cena Platform -- Item Selector
// MST-010: Selects next question using frontier + Elo + 85% rule + interleaving
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Selects the next assessment item for a student using the learning frontier,
/// Elo-calibrated difficulty targeting 85% expected correctness, and interleaving.
/// </summary>
public static class ItemSelector
{
    private const float TargetCorrectness = 0.85f;

    /// <summary>
    /// Select the next item for the student.
    /// Step 1: Pick target concept from frontier (highest rank).
    /// Step 2: With interleaving probability, switch to a different topic cluster.
    /// Step 3: Pick item closest to 85% expected correctness.
    /// </summary>
    public static ItemCandidate? SelectNext(
        IReadOnlyList<FrontierConcept> frontier,
        float studentTheta,
        IReadOnlyList<ItemCandidate> availableItems,
        string? lastConceptId,
        float interleavingProbability = 0.5f)
    {
        if (frontier.Count == 0 || availableItems.Count == 0)
            return null;

        // Step 1: Pick target concept
        string targetConceptId = frontier[0].ConceptId;
        string targetCluster = frontier[0].TopicCluster;

        // Step 2: Interleaving — switch to different cluster if coin flip succeeds
        if (lastConceptId != null && interleavingProbability > 0f)
        {
            bool shouldInterleave = interleavingProbability >= 1.0f ||
                Random.Shared.NextDouble() < interleavingProbability;

            if (shouldInterleave)
            {
                var differentCluster = frontier
                    .FirstOrDefault(f => f.TopicCluster != GetClusterForConcept(frontier, lastConceptId));

                if (differentCluster != null)
                {
                    targetConceptId = differentCluster.ConceptId;
                    targetCluster = differentCluster.TopicCluster;
                }
            }
        }

        // Step 3: Pick item for target concept closest to 85% correctness
        var selected = SelectItemForConcept(targetConceptId, studentTheta, availableItems);

        // Fallback: try other frontier concepts if no items match
        if (selected == null)
        {
            foreach (var concept in frontier)
            {
                if (concept.ConceptId == targetConceptId) continue;
                selected = SelectItemForConcept(concept.ConceptId, studentTheta, availableItems);
                if (selected != null) break;
            }
        }

        return selected;
    }

    private static ItemCandidate? SelectItemForConcept(
        string conceptId,
        float studentTheta,
        IReadOnlyList<ItemCandidate> availableItems)
    {
        ItemCandidate? best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < availableItems.Count; i++)
        {
            var item = availableItems[i];
            if (item.ConceptId != conceptId) continue;

            // Compute expected correctness for this student-item pair
            float expected = item.ExpectedCorrectness > 0
                ? item.ExpectedCorrectness
                : EloScoring.ExpectedCorrectness(studentTheta, item.DifficultyElo);

            float distance = MathF.Abs(expected - TargetCorrectness);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item;
            }
        }

        return best;
    }

    private static string? GetClusterForConcept(IReadOnlyList<FrontierConcept> frontier, string conceptId)
    {
        for (int i = 0; i < frontier.Count; i++)
        {
            if (frontier[i].ConceptId == conceptId)
                return frontier[i].TopicCluster;
        }
        return null;
    }
}
