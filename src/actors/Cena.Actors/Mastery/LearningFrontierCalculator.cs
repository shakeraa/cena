// =============================================================================
// Cena Platform -- Learning Frontier Calculator
// MST-009: Determines what concepts a student is ready to learn next
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// A concept on the learning frontier, ranked by composite score.
/// </summary>
public sealed record FrontierConcept(
    string ConceptId,
    string Name,
    string TopicCluster,
    float PSI,
    float CurrentMastery,
    float InformationGain,
    float ReviewUrgency,
    float CompositeRank);

/// <summary>
/// Configuration for frontier ranking weights.
/// </summary>
public sealed record FrontierConfig(
    float InformationGainWeight = 0.40f,
    float ReviewUrgencyWeight = 0.30f,
    float PsiWeight = 0.20f,
    float InterleavingWeight = 0.10f,
    float PsiThreshold = 0.80f,
    float MasteryUpperBound = 0.90f)
{
    public static readonly FrontierConfig Default = new();
}

/// <summary>
/// Computes the learning frontier: concepts the student is ready to learn.
/// Filter: PSI >= 0.8 AND mastery &lt; 0.90.
/// Ranked by composite score for optimal learning path.
/// </summary>
public static class LearningFrontierCalculator
{
    /// <summary>
    /// Compute the learning frontier for a student.
    /// </summary>
    public static IReadOnlyList<FrontierConcept> ComputeFrontier(
        IReadOnlyDictionary<string, ConceptMasteryState> overlay,
        IConceptGraphCache graphCache,
        DateTimeOffset now,
        int maxResults = 20,
        string? lastTopicCluster = null,
        FrontierConfig? config = null)
    {
        config ??= FrontierConfig.Default;
        var candidates = new List<FrontierConcept>();

        foreach (var (conceptId, node) in graphCache.Concepts)
        {
            // Compute PSI for this concept
            float psi = PrerequisiteSatisfactionIndex.Compute(conceptId, overlay, graphCache);
            if (psi < config.PsiThreshold)
                continue;

            // Get current mastery (0 if never encountered)
            float currentMastery = overlay.TryGetValue(conceptId, out var state)
                ? state.MasteryProbability
                : 0.0f;

            // Already mastered — not on frontier
            if (currentMastery >= config.MasteryUpperBound)
                continue;

            // Compute ranking signals
            int attemptCount = state?.AttemptCount ?? 0;
            float informationGain = 1.0f / (1 + attemptCount);

            float reviewUrgency = 0f;
            if (state != null && state.LastInteraction != default && state.HalfLifeHours > 0)
            {
                float recall = state.RecallProbability(now);
                reviewUrgency = Math.Max(0f, MasteryConstants.RecallReviewThresholdF - recall);
            }

            float interleavingBonus = (lastTopicCluster != null && node.TopicCluster != lastTopicCluster)
                ? 1.0f : 0.0f;

            float compositeRank =
                config.InformationGainWeight * informationGain +
                config.ReviewUrgencyWeight * reviewUrgency +
                config.PsiWeight * psi +
                config.InterleavingWeight * interleavingBonus;

            candidates.Add(new FrontierConcept(
                conceptId, node.Name, node.TopicCluster,
                psi, currentMastery, informationGain, reviewUrgency, compositeRank));
        }

        candidates.Sort((a, b) => b.CompositeRank.CompareTo(a.CompositeRank));
        return candidates.Count <= maxResults ? candidates : candidates.GetRange(0, maxResults);
    }
}
