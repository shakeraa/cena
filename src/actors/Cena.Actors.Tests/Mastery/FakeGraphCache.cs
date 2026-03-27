// =============================================================================
// Test helper: in-memory graph cache for prerequisite calculator tests
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class FakeGraphCache : IConceptGraphCache
{
    private readonly Dictionary<string, List<MasteryPrerequisiteEdge>> _prerequisites;
    private readonly Dictionary<string, List<string>> _descendants;
    private readonly Dictionary<string, int> _depths;

    public FakeGraphCache(
        Dictionary<string, List<MasteryPrerequisiteEdge>>? prerequisites = null,
        Dictionary<string, List<string>>? descendants = null,
        Dictionary<string, int>? depths = null)
    {
        _prerequisites = prerequisites ?? new();
        _descendants = descendants ?? new();
        _depths = depths ?? new();
    }

    public IReadOnlyList<MasteryPrerequisiteEdge> GetPrerequisites(string conceptId) =>
        _prerequisites.TryGetValue(conceptId, out var edges) ? edges : Array.Empty<MasteryPrerequisiteEdge>();

    public IReadOnlyList<string> GetDescendants(string conceptId) =>
        _descendants.TryGetValue(conceptId, out var desc) ? desc : Array.Empty<string>();

    public int GetDepth(string conceptId) =>
        _depths.TryGetValue(conceptId, out var d) ? d : 0;

    public IReadOnlyDictionary<string, MasteryConceptNode> Concepts { get; } =
        new Dictionary<string, MasteryConceptNode>();
}
