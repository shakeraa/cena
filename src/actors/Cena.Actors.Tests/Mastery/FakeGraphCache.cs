// =============================================================================
// Test helper: in-memory graph cache for mastery engine tests
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class FakeGraphCache : IConceptGraphCache
{
    private readonly Dictionary<string, List<MasteryPrerequisiteEdge>> _prerequisites;
    private readonly Dictionary<string, List<string>> _descendants;
    private readonly Dictionary<string, int> _depths;
    private readonly Dictionary<string, MasteryConceptNode> _concepts;

    public FakeGraphCache(
        Dictionary<string, List<MasteryPrerequisiteEdge>>? prerequisites = null,
        Dictionary<string, List<string>>? descendants = null,
        Dictionary<string, int>? depths = null,
        Dictionary<string, MasteryConceptNode>? concepts = null,
        Dictionary<string, int>? descendantCounts = null)
    {
        _prerequisites = prerequisites ?? new();
        _descendants = descendants ?? new();
        _depths = depths ?? new();
        _concepts = concepts ?? new();

        // Convenience: build descendants from descendantCounts
        if (descendantCounts != null)
        {
            foreach (var (id, count) in descendantCounts)
            {
                if (!_descendants.ContainsKey(id))
                    _descendants[id] = Enumerable.Range(0, count).Select(i => $"{id}-dep-{i}").ToList();
            }
        }
    }

    public IReadOnlyList<MasteryPrerequisiteEdge> GetPrerequisites(string conceptId) =>
        _prerequisites.TryGetValue(conceptId, out var edges) ? edges : Array.Empty<MasteryPrerequisiteEdge>();

    public IReadOnlyList<string> GetDescendants(string conceptId) =>
        _descendants.TryGetValue(conceptId, out var desc) ? desc : Array.Empty<string>();

    public int GetDepth(string conceptId) =>
        _depths.TryGetValue(conceptId, out var d) ? d : 0;

    public IReadOnlyDictionary<string, MasteryConceptNode> Concepts => _concepts;
}
