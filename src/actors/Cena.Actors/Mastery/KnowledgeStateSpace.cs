// =============================================================================
// Cena Platform -- Knowledge State Space Builder
// MST-013: Builds feasible (downward-closed) knowledge states from graph
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>
/// Builds the space of feasible knowledge states from a concept prerequisite graph.
/// A state is feasible if for every concept in the state, all prerequisites are also present.
/// </summary>
public static class KnowledgeStateSpace
{
    private const int SmallGraphThreshold = 20;

    /// <summary>
    /// Build feasible states. For small graphs (&lt;20), enumerate all.
    /// For large graphs, sample random feasible states by topological inclusion.
    /// </summary>
    public static IReadOnlyList<KnowledgeState> BuildFeasibleStates(
        IConceptGraphCache graphCache,
        int maxStates = 500)
    {
        var conceptIds = graphCache.Concepts.Keys.ToList();

        if (conceptIds.Count == 0)
            return new[] { new KnowledgeState(ImmutableHashSet<string>.Empty) };

        if (conceptIds.Count < SmallGraphThreshold)
            return EnumerateAll(conceptIds, graphCache);

        return SampleFeasible(conceptIds, graphCache, maxStates);
    }

    private static IReadOnlyList<KnowledgeState> EnumerateAll(
        List<string> conceptIds, IConceptGraphCache graphCache)
    {
        var results = new List<KnowledgeState>();
        int n = conceptIds.Count;
        int total = 1 << n; // 2^n subsets

        for (int mask = 0; mask < total; mask++)
        {
            var set = ImmutableHashSet.CreateBuilder<string>();
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) != 0)
                    set.Add(conceptIds[i]);
            }

            if (IsFeasible(set.ToImmutable(), graphCache))
                results.Add(new KnowledgeState(set.ToImmutable()));
        }

        return results;
    }

    private static IReadOnlyList<KnowledgeState> SampleFeasible(
        List<string> conceptIds, IConceptGraphCache graphCache, int maxStates)
    {
        var results = new HashSet<KnowledgeState>();

        // Always include empty and full states
        results.Add(new KnowledgeState(ImmutableHashSet<string>.Empty));

        var fullSet = ImmutableHashSet.CreateRange(conceptIds);
        if (IsFeasible(fullSet, graphCache))
            results.Add(new KnowledgeState(fullSet));

        // Sort by depth (topological order) for inclusion sampling
        var sorted = conceptIds
            .OrderBy(c => graphCache.GetDepth(c))
            .ToList();

        var rng = new Random(42); // deterministic for reproducibility

        int attempts = 0;
        while (results.Count < maxStates && attempts < maxStates * 10)
        {
            attempts++;
            var builder = ImmutableHashSet.CreateBuilder<string>();

            // Include each concept with some probability, respecting topological order
            float inclusionRate = (float)rng.NextDouble();
            foreach (var concept in sorted)
            {
                if (rng.NextDouble() < inclusionRate)
                {
                    // Only include if all prerequisites are already included
                    var prereqs = graphCache.GetPrerequisites(concept);
                    bool allPrereqsMet = true;
                    for (int i = 0; i < prereqs.Count; i++)
                    {
                        if (!builder.Contains(prereqs[i].SourceConceptId))
                        {
                            allPrereqsMet = false;
                            break;
                        }
                    }
                    if (allPrereqsMet)
                        builder.Add(concept);
                }
            }

            results.Add(new KnowledgeState(builder.ToImmutable()));
        }

        return results.ToList();
    }

    private static bool IsFeasible(ImmutableHashSet<string> state, IConceptGraphCache graphCache)
    {
        foreach (var concept in state)
        {
            var prereqs = graphCache.GetPrerequisites(concept);
            for (int i = 0; i < prereqs.Count; i++)
            {
                if (!state.Contains(prereqs[i].SourceConceptId))
                    return false;
            }
        }
        return true;
    }
}
