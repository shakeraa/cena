// =============================================================================
// Cena Platform -- PrerequisiteEnforcementService (Domain Service)
// Layer: Domain Services | Runtime: .NET 9
//
// Enforces prerequisite gates in the curriculum knowledge graph.
// Dual thresholds: ProgressionThreshold=0.85, PrerequisiteGateThreshold=0.95
//
// ProgressionThreshold (0.85): the student has demonstrated sufficient mastery
//   to proceed to dependent concepts. Used for frontier computation.
//
// PrerequisiteGateThreshold (0.95): a stricter gate used for concepts that
//   are critical prerequisites (e.g., foundational algebra for calculus).
//   Prevents premature progression on high-stakes dependency chains.
// =============================================================================

using Cena.Actors.Graph;

namespace Cena.Actors.Services;

// =============================================================================
// RESULT TYPES
// =============================================================================

/// <summary>
/// Result of checking whether a concept's prerequisites are satisfied.
/// </summary>
public sealed record PrerequisiteCheckResult(
    string ConceptId,
    bool AllPrerequisitesMet,
    IReadOnlyList<UnmetPrerequisite> UnmetPrerequisites,
    int TotalPrerequisiteCount);

/// <summary>
/// Details of a single unmet prerequisite, including the gap to threshold.
/// </summary>
public sealed record UnmetPrerequisite(
    string PrerequisiteConceptId,
    double CurrentMastery,
    double RequiredThreshold,
    double Gap);

/// <summary>
/// A concept that is blocked by one or more unmet prerequisites.
/// </summary>
public sealed record BlockedConcept(
    string ConceptId,
    IReadOnlyList<string> BlockingPrerequisiteIds);

// =============================================================================
// INTERFACE
// =============================================================================

public interface IPrerequisiteEnforcementService
{
    /// <summary>
    /// Check whether all prerequisites for a given concept are satisfied.
    /// Uses dual thresholds: edges with Weight >= 0.9 use the stricter
    /// PrerequisiteGateThreshold (0.95); others use ProgressionThreshold (0.85).
    /// </summary>
    /// <param name="conceptId">The concept to check prerequisites for.</param>
    /// <param name="masteryMap">Student's current mastery map (conceptId -> P(known)).</param>
    /// <param name="prerequisiteEdges">
    /// All prerequisite edges targeting this concept.
    /// Edge.FromConceptId = prerequisite, Edge.ToConceptId = this concept.
    /// </param>
    /// <returns>Detailed prerequisite check result.</returns>
    PrerequisiteCheckResult CheckPrerequisites(
        string conceptId,
        IReadOnlyDictionary<string, double> masteryMap,
        IReadOnlyList<PrerequisiteEdge> prerequisiteEdges);

    /// <summary>
    /// Compute all blocked concepts: concepts that have at least one unmet prerequisite.
    /// Performs a full graph traversal over all prerequisite edges.
    /// </summary>
    /// <param name="masteryMap">Student's current mastery map.</param>
    /// <param name="allPrerequisites">
    /// Complete prerequisite edge list from the curriculum graph.
    /// Keyed by target concept ID -> list of prerequisite edges.
    /// </param>
    /// <returns>List of blocked concepts with their blocking prerequisites.</returns>
    List<BlockedConcept> GetBlockedConcepts(
        IReadOnlyDictionary<string, double> masteryMap,
        IReadOnlyDictionary<string, List<PrerequisiteEdge>> allPrerequisites);

    /// <summary>
    /// Compute the learning frontier: concepts whose prerequisites are ALL met
    /// and which are NOT yet mastered themselves.
    /// A concept with no prerequisites is always on the frontier (if not mastered).
    /// </summary>
    /// <param name="masteryMap">Student's current mastery map.</param>
    /// <param name="allPrerequisites">
    /// Complete prerequisite edge list from the curriculum graph.
    /// Keyed by target concept ID -> list of prerequisite edges.
    /// </param>
    /// <returns>Ordered list of frontier concept IDs (highest-priority first).</returns>
    List<string> GetUnlockedFrontier(
        IReadOnlyDictionary<string, double> masteryMap,
        IReadOnlyDictionary<string, List<PrerequisiteEdge>> allPrerequisites);
}

// =============================================================================
// IMPLEMENTATION
// =============================================================================

/// <summary>
/// Production prerequisite enforcement using dual-threshold gating.
///
/// Threshold selection per edge:
///   - Edges with Weight >= 0.9 (critical prerequisites): use PrerequisiteGateThreshold (0.95)
///   - Edges with Weight < 0.9 (standard prerequisites): use ProgressionThreshold (0.85)
///
/// This dual-threshold approach prevents premature progression on critical
/// dependency chains while allowing more flexible exploration on weaker dependencies.
/// </summary>
public sealed class PrerequisiteEnforcementService : IPrerequisiteEnforcementService
{
    /// <summary>
    /// Standard progression threshold: student has demonstrated sufficient mastery
    /// to proceed. Matches BktParameters.Default.ProgressionThreshold.
    /// </summary>
    public const double ProgressionThreshold = MasteryConstants.ProgressionThreshold;

    /// <summary>
    /// Strict prerequisite gate threshold: used for critical prerequisites (weight >= 0.9).
    /// Prevents premature progression on foundational concepts.
    /// Matches BktParameters.Default.PrerequisiteGateThreshold.
    /// </summary>
    public const double PrerequisiteGateThreshold = MasteryConstants.PrerequisiteGateThreshold;

    /// <summary>
    /// Edge weight threshold above which the stricter PrerequisiteGateThreshold applies.
    /// </summary>
    private const double CriticalEdgeWeight = 0.9;

    public PrerequisiteCheckResult CheckPrerequisites(
        string conceptId,
        IReadOnlyDictionary<string, double> masteryMap,
        IReadOnlyList<PrerequisiteEdge> prerequisiteEdges)
    {
        if (prerequisiteEdges.Count == 0)
        {
            // No prerequisites: concept is always unlocked
            return new PrerequisiteCheckResult(
                ConceptId: conceptId,
                AllPrerequisitesMet: true,
                UnmetPrerequisites: Array.Empty<UnmetPrerequisite>(),
                TotalPrerequisiteCount: 0);
        }

        var unmet = new List<UnmetPrerequisite>();

        foreach (var edge in prerequisiteEdges)
        {
            // Select threshold based on edge weight:
            // Critical edges (weight >= 0.9) require higher mastery
            double requiredThreshold = edge.Weight >= CriticalEdgeWeight
                ? PrerequisiteGateThreshold
                : ProgressionThreshold;

            // Look up current mastery; default to 0.0 if concept not yet attempted
            double currentMastery = masteryMap.GetValueOrDefault(edge.FromConceptId, 0.0);

            if (currentMastery < requiredThreshold)
            {
                double gap = requiredThreshold - currentMastery;
                unmet.Add(new UnmetPrerequisite(
                    PrerequisiteConceptId: edge.FromConceptId,
                    CurrentMastery: currentMastery,
                    RequiredThreshold: requiredThreshold,
                    Gap: gap));
            }
        }

        return new PrerequisiteCheckResult(
            ConceptId: conceptId,
            AllPrerequisitesMet: unmet.Count == 0,
            UnmetPrerequisites: unmet,
            TotalPrerequisiteCount: prerequisiteEdges.Count);
    }

    public List<BlockedConcept> GetBlockedConcepts(
        IReadOnlyDictionary<string, double> masteryMap,
        IReadOnlyDictionary<string, List<PrerequisiteEdge>> allPrerequisites)
    {
        var blocked = new List<BlockedConcept>();

        foreach (var (targetConceptId, edges) in allPrerequisites)
        {
            var blockingIds = new List<string>();

            foreach (var edge in edges)
            {
                double requiredThreshold = edge.Weight >= CriticalEdgeWeight
                    ? PrerequisiteGateThreshold
                    : ProgressionThreshold;

                double currentMastery = masteryMap.GetValueOrDefault(edge.FromConceptId, 0.0);

                if (currentMastery < requiredThreshold)
                {
                    blockingIds.Add(edge.FromConceptId);
                }
            }

            if (blockingIds.Count > 0)
            {
                blocked.Add(new BlockedConcept(
                    ConceptId: targetConceptId,
                    BlockingPrerequisiteIds: blockingIds));
            }
        }

        return blocked;
    }

    public List<string> GetUnlockedFrontier(
        IReadOnlyDictionary<string, double> masteryMap,
        IReadOnlyDictionary<string, List<PrerequisiteEdge>> allPrerequisites)
    {
        // Collect all concept IDs that appear in the graph:
        // - As targets of prerequisite edges (they have prerequisites)
        // - As sources of prerequisite edges (they are prerequisites for others)
        var allConceptIds = new HashSet<string>();

        foreach (var (targetId, edges) in allPrerequisites)
        {
            allConceptIds.Add(targetId);
            foreach (var edge in edges)
            {
                allConceptIds.Add(edge.FromConceptId);
            }
        }

        // Also include any concepts the student has attempted but might not
        // appear in the prerequisite graph (leaf concepts with no dependents)
        foreach (var conceptId in masteryMap.Keys)
        {
            allConceptIds.Add(conceptId);
        }

        var frontier = new List<string>();

        foreach (var conceptId in allConceptIds)
        {
            // Skip concepts already mastered at the progression threshold
            double currentMastery = masteryMap.GetValueOrDefault(conceptId, 0.0);
            if (currentMastery >= ProgressionThreshold)
                continue;

            // Check if all prerequisites are met
            bool allPrereqsMet = true;

            if (allPrerequisites.TryGetValue(conceptId, out var prereqEdges))
            {
                foreach (var edge in prereqEdges)
                {
                    double requiredThreshold = edge.Weight >= CriticalEdgeWeight
                        ? PrerequisiteGateThreshold
                        : ProgressionThreshold;

                    double prereqMastery = masteryMap.GetValueOrDefault(edge.FromConceptId, 0.0);

                    if (prereqMastery < requiredThreshold)
                    {
                        allPrereqsMet = false;
                        break;
                    }
                }
            }
            // Concepts with no prerequisites: allPrereqsMet remains true

            if (allPrereqsMet)
            {
                frontier.Add(conceptId);
            }
        }

        // Sort frontier by mastery descending: concepts closer to mastery threshold
        // are prioritized (the student is almost there, so finishing them first
        // maximizes unlock potential for downstream concepts)
        frontier.Sort((a, b) =>
        {
            double masteryA = masteryMap.GetValueOrDefault(a, 0.0);
            double masteryB = masteryMap.GetValueOrDefault(b, 0.0);
            return masteryB.CompareTo(masteryA);
        });

        return frontier;
    }
}
