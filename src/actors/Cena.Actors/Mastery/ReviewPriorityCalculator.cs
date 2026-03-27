// =============================================================================
// Cena Platform -- Review Priority Calculator
// MST-008: Ranks decayed concepts by review urgency
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// A concept that needs review, ranked by priority.
/// </summary>
public sealed record ReviewPriorityItem(
    string ConceptId,
    float RecallProbability,
    int DescendantCount,
    float Priority);

/// <summary>
/// Computes review priority for decayed concepts.
/// Foundational concepts with many dependents are prioritized even for small decay.
/// </summary>
public static class ReviewPriorityCalculator
{
    private const float ReviewThreshold = 0.85f;
    private const float MinMasteryForReview = 0.70f;

    /// <summary>
    /// Priority formula: (0.85 - recall) * (1 + log2(max(descendants, 1))).
    /// Returns 0.0 if recall >= 0.85 (no review needed).
    /// </summary>
    public static float ComputePriority(float recallProbability, int descendantCount)
    {
        if (recallProbability >= ReviewThreshold)
            return 0.0f;

        float urgency = ReviewThreshold - recallProbability;
        float impact = 1.0f + (float)Math.Log2(Math.Max(descendantCount, 1));
        return urgency * impact;
    }

    /// <summary>
    /// Rank all review-worthy concepts for a student, sorted by priority descending.
    /// Filters to concepts where mastery >= 0.70 and recall &lt; 0.85.
    /// </summary>
    public static IReadOnlyList<ReviewPriorityItem> RankReviewConcepts(
        IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay,
        IConceptGraphCache graphCache,
        DateTimeOffset now,
        int maxResults = 10)
    {
        var items = new List<ReviewPriorityItem>();

        foreach (var (conceptId, state) in masteryOverlay)
        {
            if (state.MasteryProbability < MinMasteryForReview)
                continue;

            float recall = state.RecallProbability(now);
            if (recall >= ReviewThreshold)
                continue;

            int descendants = graphCache.GetDescendants(conceptId).Count;
            float priority = ComputePriority(recall, descendants);

            if (priority > 0f)
                items.Add(new ReviewPriorityItem(conceptId, recall, descendants, priority));
        }

        items.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        return items.Count <= maxResults ? items : items.GetRange(0, maxResults);
    }
}
