// =============================================================================
// Cena Platform — Stub Ministry Corpus for waterfall tests (prr-201)
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

internal sealed class StubMinistryReferenceCorpus : IMinistryReferenceCorpus
{
    private readonly List<MinistryReferenceItem> _items;

    public StubMinistryReferenceCorpus(params (string Id, string Stem)[] items)
    {
        _items = items
            .Select(i => new MinistryReferenceItem(
                i.Id, MinistrySimilarityChecker.Normalise(i.Stem)))
            .ToList();
    }

    public IReadOnlyList<MinistryReferenceItem> GetReferences(string subject, string trackKey)
        => _items;
}
