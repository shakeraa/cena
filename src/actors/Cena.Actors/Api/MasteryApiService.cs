// =============================================================================
// Cena Platform -- Mastery API Service
// MST-017: Service layer bridging StudentActor state to REST API DTOs
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Api;

/// <summary>
/// Service layer that transforms StudentActor mastery state into API DTOs.
/// All methods are pure computation over in-memory state — no I/O.
/// The endpoint layer handles actor communication and authorization.
/// </summary>
public static class MasteryApiService
{
    /// <summary>
    /// Build the full mastery response for a student's subject.
    /// Computes recall and effective mastery at request time.
    /// </summary>
    public static StudentMasteryResponse BuildStudentMastery(
        string studentId,
        IReadOnlyDictionary<string, ConceptMasteryState> overlay,
        IConceptGraphCache? graphCache,
        DateTimeOffset now,
        string? subjectFilter = null)
    {
        var dtos = new List<ConceptMasteryDto>();

        foreach (var (conceptId, state) in overlay)
        {
            // Filter by subject if graph cache available
            if (subjectFilter != null && graphCache != null)
            {
                if (graphCache.Concepts.TryGetValue(conceptId, out var node) &&
                    !string.Equals(node.Subject, subjectFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            string? name = null;
            string? cluster = null;
            if (graphCache?.Concepts.TryGetValue(conceptId, out var conceptNode) == true)
            {
                name = conceptNode.Name;
                cluster = conceptNode.TopicCluster;
            }

            float recall = state.RecallProbability(now);
            float prereqSupport = graphCache != null
                ? PrerequisiteCalculator.ComputeSupport(conceptId, overlay, graphCache)
                : 1.0f;
            float effective = EffectiveMasteryCalculator.Compute(state, prereqSupport, now);

            dtos.Add(new ConceptMasteryDto(
                conceptId, name, cluster,
                state.MasteryProbability,
                recall,
                effective,
                state.HalfLifeHours,
                state.BloomLevel,
                state.QualityQuadrant.ToString(),
                state.MasteryLevel.ToString(),
                state.LastInteraction,
                state.AttemptCount,
                state.CorrectCount,
                state.CurrentStreak));
        }

        int mastered = dtos.Count(d => d.MasteryProbability >= MasteryThreshold.Proficient);
        float overall = dtos.Count > 0
            ? dtos.Average(d => d.EffectiveMastery)
            : 0f;

        return new StudentMasteryResponse(studentId, dtos, dtos.Count, mastered, overall);
    }

    /// <summary>
    /// Build topic progress aggregation for a cluster.
    /// </summary>
    public static TopicProgressDto BuildTopicProgress(
        IReadOnlyDictionary<string, ConceptMasteryState> overlay,
        IConceptGraphCache graphCache,
        string topicClusterId,
        DateTimeOffset now)
    {
        var conceptsInCluster = graphCache.Concepts
            .Where(kv => string.Equals(kv.Value.TopicCluster, topicClusterId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (conceptsInCluster.Count == 0)
            return new TopicProgressDto(topicClusterId, null, 0, 0, 0f, null);

        float totalMastery = 0f;
        int masteredCount = 0;
        ConceptMasteryDto? weakest = null;
        float weakestEffective = float.MaxValue;

        foreach (var (conceptId, node) in conceptsInCluster)
        {
            float mastery = 0f;
            float effective = 0f;

            if (overlay.TryGetValue(conceptId, out var state))
            {
                mastery = state.MasteryProbability;
                float recall = state.RecallProbability(now);
                float prereq = PrerequisiteCalculator.ComputeSupport(conceptId, overlay, graphCache);
                effective = EffectiveMasteryCalculator.Compute(state, prereq, now);

                if (mastery >= MasteryThreshold.Proficient)
                    masteredCount++;
            }

            totalMastery += effective;

            if (effective < weakestEffective)
            {
                weakestEffective = effective;
                var s = overlay.GetValueOrDefault(conceptId) ?? new ConceptMasteryState();
                weakest = new ConceptMasteryDto(
                    conceptId, node.Name, node.TopicCluster,
                    s.MasteryProbability, s.RecallProbability(now), effective,
                    s.HalfLifeHours, s.BloomLevel, s.QualityQuadrant.ToString(),
                    s.MasteryLevel.ToString(), s.LastInteraction,
                    s.AttemptCount, s.CorrectCount, s.CurrentStreak);
            }
        }

        float avg = conceptsInCluster.Count > 0 ? totalMastery / conceptsInCluster.Count : 0f;
        string? clusterName = conceptsInCluster.FirstOrDefault().Value?.Name;

        return new TopicProgressDto(topicClusterId, clusterName, conceptsInCluster.Count,
            masteredCount, avg, weakest);
    }

    /// <summary>
    /// Build learning frontier DTOs from the calculator output.
    /// </summary>
    public static IReadOnlyList<FrontierConceptDto> BuildFrontier(
        IReadOnlyDictionary<string, ConceptMasteryState> overlay,
        IConceptGraphCache graphCache,
        DateTimeOffset now,
        int maxResults = 10)
    {
        var frontier = LearningFrontierCalculator.ComputeFrontier(
            overlay, graphCache, now, maxResults);

        return frontier.Select(f => new FrontierConceptDto(
            f.ConceptId, f.Name, f.TopicCluster,
            f.PSI, f.CurrentMastery, f.CompositeRank)).ToList();
    }

    /// <summary>
    /// Build decay alerts from review priority calculator.
    /// </summary>
    public static IReadOnlyList<DecayAlertDto> BuildDecayAlerts(
        IReadOnlyDictionary<string, ConceptMasteryState> overlay,
        IConceptGraphCache graphCache,
        DateTimeOffset now,
        int maxResults = 20)
    {
        var ranked = ReviewPriorityCalculator.RankReviewConcepts(
            overlay, graphCache, now, maxResults);

        return ranked.Select(r =>
        {
            string? name = graphCache.Concepts.TryGetValue(r.ConceptId, out var node) ? node.Name : null;
            float hoursSince = overlay.TryGetValue(r.ConceptId, out var state) && state.LastInteraction != default
                ? (float)(now - state.LastInteraction).TotalHours
                : 0f;
            return new DecayAlertDto(r.ConceptId, name, r.RecallProbability, hoursSince, r.Priority);
        }).ToList();
    }
}
