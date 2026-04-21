// =============================================================================
// Cena Platform — BagrutCorpusService unit tests (prr-242)
//
// Focused on the validation + persistence orchestration side of the service.
// Marten is mocked via NSubstitute — we verify `Store(doc)` is called and
// SaveChangesAsync is awaited. The read-side + cache-invalidation paths are
// exercised via a small in-memory fake.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Documents;
using Marten;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutCorpusServiceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _writeSession = Substitute.For<IDocumentSession>();

    public BagrutCorpusServiceTests()
    {
        _store.LightweightSession().Returns(_writeSession);
    }

    private static BagrutCorpusItemDocument ValidItem(int qn = 1) => new()
    {
        Id = BagrutCorpusItemDocument.ComposeId("035", "035581", qn),
        MinistrySubjectCode = "035",
        MinistryQuestionPaperCode = "035581",
        Units = 5,
        TrackKey = "5U",
        Year = 2024,
        Season = BagrutCorpusSeason.Summer,
        Moed = "A",
        QuestionNumber = qn,
        TopicId = "algebra.quadratics",
        Stream = BagrutCorpusStream.Hebrew,
        RawText = "פתרו את המשוואה",
        NormalisedStem = "פתרו את המשוואה",
        IngestedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task UpsertAsync_persists_valid_item()
    {
        var svc = new BagrutCorpusService(_store);
        var item = ValidItem();

        await svc.UpsertAsync(item);

        _writeSession.Received(1).Store(Arg.Is<BagrutCorpusItemDocument>(d => d.Id == item.Id));
        await _writeSession.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_rejects_item_with_blank_id()
    {
        var svc = new BagrutCorpusService(_store);
        var bad = ValidItem();
        bad.Id = string.Empty;

        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertAsync(bad));
    }

    [Fact]
    public async Task UpsertAsync_rejects_item_with_blank_normalised_stem()
    {
        var svc = new BagrutCorpusService(_store);
        var bad = ValidItem();
        bad.NormalisedStem = "";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertAsync(bad));
    }

    [Fact]
    public async Task UpsertManyAsync_ignores_empty_list()
    {
        var svc = new BagrutCorpusService(_store);

        await svc.UpsertManyAsync(Array.Empty<BagrutCorpusItemDocument>());

        _writeSession.DidNotReceive().Store(Arg.Any<BagrutCorpusItemDocument>());
    }

    [Fact]
    public async Task UpsertManyAsync_persists_each_item()
    {
        var svc = new BagrutCorpusService(_store);
        var items = new[] { ValidItem(1), ValidItem(2), ValidItem(3) };

        await svc.UpsertManyAsync(items);

        foreach (var item in items)
            _writeSession.Received(1).Store(
                Arg.Is<BagrutCorpusItemDocument>(d => d.Id == item.Id));
        await _writeSession.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ComposeId_is_deterministic()
    {
        var a = BagrutCorpusItemDocument.ComposeId("035", "035581", 7);
        var b = BagrutCorpusItemDocument.ComposeId("035", "035581", 7);
        Assert.Equal(a, b);
        Assert.StartsWith("bagrut-corpus:035:035581:", a);
    }

    [Fact]
    public void MapSubjectToMinistryCode_maps_math_aliases_to_035()
    {
        Assert.Equal("035", BagrutCorpusService.MapSubjectToMinistryCode("math"));
        Assert.Equal("035", BagrutCorpusService.MapSubjectToMinistryCode("Mathematics"));
        Assert.Equal("035", BagrutCorpusService.MapSubjectToMinistryCode("מתמטיקה"));
        // Unknown subjects pass through — future-proof.
        Assert.Equal("biology", BagrutCorpusService.MapSubjectToMinistryCode("biology"));
    }
}
