// =============================================================================
// Cena Platform -- Concept Graph Cache Interface
// MST-004: In-memory graph cache for prerequisite lookups (O(1), no network)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Prerequisite edge in the concept graph.
/// </summary>
public sealed record MasteryPrerequisiteEdge(
    string SourceConceptId,
    string TargetConceptId,
    float Strength);

/// <summary>
/// Concept node with curriculum metadata for mastery computations.
/// </summary>
public sealed record MasteryConceptNode(
    string Id,
    string Name,
    string Subject,
    string TopicCluster,
    int DepthLevel,
    float IntrinsicLoad,
    float BagrutWeight,
    int BloomMax);

/// <summary>
/// In-memory graph cache loaded from Neo4j at actor startup.
/// All lookups are O(1) dictionary-based -- no network calls on hot path.
/// </summary>
public interface IConceptGraphCache
{
    IReadOnlyList<MasteryPrerequisiteEdge> GetPrerequisites(string conceptId);
    IReadOnlyList<string> GetDescendants(string conceptId);
    int GetDepth(string conceptId);
    IReadOnlyDictionary<string, MasteryConceptNode> Concepts { get; }
}
