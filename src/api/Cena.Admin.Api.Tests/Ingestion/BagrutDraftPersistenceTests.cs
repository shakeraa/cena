// =============================================================================
// Cena Platform -- BagrutDraftPersistence tests (curator metadata auto-fill)
//
// Verifies the 2026-04-29 fix that pre-populates AutoExtractedMetadata on
// every Bagrut draft so the curator UI opens with Subject/Language/
// SourceType/ExpectedFigures filled and Track/TaxonomyNode inferred from
// the examCode prefix.
//
// Marten IDocumentSession is NSubstitute'd; we capture every Store() call
// and inspect the resulting PipelineItemDocument directly.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.Ingestion;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutDraftPersistenceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly BagrutDraftPersistence _persistence;

    private readonly List<PipelineItemDocument> _storedItems = new();
    private readonly List<BagrutDraftPayloadDocument> _storedPayloads = new();

    public BagrutDraftPersistenceTests()
    {
        _store.LightweightSession().Returns(_session);

        // Capture every Store(...) call. Marten's IDocumentSession exposes
        // a params-array overload (Store<T>(T[] entities)) which is what
        // Castle dispatches to when our service calls Store(doc) — NSub
        // sees a single-element T[] argument.
        _session.WhenForAnyArgs(s => s.Store<PipelineItemDocument>(default!))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                {
                    if (arg is PipelineItemDocument doc) _storedItems.Add(doc);
                    else if (arg is PipelineItemDocument[] docs) _storedItems.AddRange(docs);
                }
            });
        _session.WhenForAnyArgs(s => s.Store<BagrutDraftPayloadDocument>(default!))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                {
                    if (arg is BagrutDraftPayloadDocument doc) _storedPayloads.Add(doc);
                    else if (arg is BagrutDraftPayloadDocument[] docs) _storedPayloads.AddRange(docs);
                }
            });

        _persistence = new BagrutDraftPersistence(
            _store, NullLogger<BagrutDraftPersistence>.Instance);
    }

    private static IngestionDraftQuestion MakeDraft(int page = 1, string examCode = "math-5u-2026-35581") =>
        new(
            DraftId: $"draft-{examCode}-p{page}",
            SourcePage: page,
            Prompt: "Question prompt",
            LatexContent: "x^2 + 2x = 3",
            AnswerChoices: Array.Empty<string>(),
            CorrectAnswer: null,
            ExamCode: examCode,
            FigureSpecJson: null,
            ExtractionConfidence: 0.9,
            ReviewNotes: new[] { "bagrut-reference:auto-extracted;requires-curator-recreation" });

    // --------------------------------------------------------------------
    // Auto-fill happy path
    // --------------------------------------------------------------------

    [Fact]
    public async Task PersistAsync_Populates_AutoExtractedMetadata_With_Five_Required_Fields()
    {
        var drafts = new[] { MakeDraft() };

        var ids = await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: "math-5u-2026-35581.pdf",
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        Assert.Single(ids);
        Assert.Single(_storedItems);

        var stored = _storedItems[0];
        Assert.Equal("auto_extracted", stored.MetadataState);
        Assert.Equal(BagrutDraftPersistence.ExtractionStrategy, stored.MetadataExtractionStrategy);

        var meta = stored.AutoExtractedMetadata;
        Assert.NotNull(meta);
        Assert.Equal("math",             meta!.Subject);
        Assert.Equal("he",               meta.Language);
        Assert.Equal("5u",               meta.Track);
        Assert.Equal("bagrut_reference", meta.SourceType);
        Assert.Equal("math_5u",          meta.TaxonomyNode);
        Assert.True(meta.ExpectedFigures);
    }

    [Fact]
    public async Task PersistAsync_Populates_FieldConfidences_With_Expected_Values()
    {
        var drafts = new[] { MakeDraft() };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Subject)]);
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Language)]);
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Track)]);          // parsed from examCode
        Assert.Equal(0.99, conf[nameof(CuratorMetadata.SourceType)]);
        Assert.Equal(0.40, conf[nameof(CuratorMetadata.TaxonomyNode)]);   // placeholder, curator drills down
        Assert.Equal(0.70, conf[nameof(CuratorMetadata.ExpectedFigures)]);
    }

    // --------------------------------------------------------------------
    // Track parsing (covers 3u, 4u, 5u, fallback)
    // --------------------------------------------------------------------

    [Theory]
    [InlineData("math-3u-2026-12345", "3u", "math_3u")]
    [InlineData("math-4u-2026-12345", "4u", "math_4u")]
    [InlineData("math-5u-2026-12345", "5u", "math_5u")]
    [InlineData("MATH-5U-2026-12345", "5u", "math_5u")]   // case-insensitive
    [InlineData("math_5u_2026_12345", "5u", "math_5u")]   // underscore separator
    public async Task PersistAsync_Parses_Track_From_ExamCode(
        string examCode, string expectedTrack, string expectedTaxonomy)
    {
        var draft = MakeDraft(examCode: examCode);

        await _persistence.PersistAsync(
            examCode: examCode,
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: new[] { draft });

        var meta = _storedItems[0].AutoExtractedMetadata!;
        Assert.Equal(expectedTrack, meta.Track);
        Assert.Equal(expectedTaxonomy, meta.TaxonomyNode);

        // Both have high(ish) confidence: track 0.95, taxonomy 0.40.
        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Track)]);
        Assert.Equal(0.40, conf[nameof(CuratorMetadata.TaxonomyNode)]);
    }

    [Theory]
    [InlineData("physics-5u-2026-12345")]   // wrong subject prefix
    [InlineData("math-2u-2026-12345")]      // unsupported track
    [InlineData("math-2026-12345")]         // missing track segment
    [InlineData("")]                        // empty
    [InlineData("   ")]                     // whitespace
    public async Task PersistAsync_Falls_Back_To_Null_Track_When_ExamCode_Unrecognised(string examCode)
    {
        // For empty examCode the underlying SourceFilename uses examCode, so
        // we override to a stable filename for the empty case.
        var draft = new IngestionDraftQuestion(
            DraftId: $"draft-fallback-{Guid.NewGuid():N}",
            SourcePage: 1,
            Prompt: "Question",
            LatexContent: "x = 1",
            AnswerChoices: Array.Empty<string>(),
            CorrectAnswer: null,
            ExamCode: examCode,
            FigureSpecJson: null,
            ExtractionConfidence: 0.9,
            ReviewNotes: Array.Empty<string>());

        await _persistence.PersistAsync(
            examCode: examCode,
            sourcePdfId: "pdf-abc",
            sourceFilename: "fallback.pdf",
            submittedBy: "curator@cena.dev",
            drafts: new[] { draft });

        var meta = _storedItems[0].AutoExtractedMetadata!;
        Assert.Null(meta.Track);
        Assert.Null(meta.TaxonomyNode);

        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.5, conf[nameof(CuratorMetadata.Track)]);
        Assert.Equal(0.2, conf[nameof(CuratorMetadata.TaxonomyNode)]);

        // The fixed fields stay populated even when the parse fails.
        Assert.Equal("math",             meta.Subject);
        Assert.Equal("he",               meta.Language);
        Assert.Equal("bagrut_reference", meta.SourceType);
        Assert.True(meta.ExpectedFigures);
    }

    // --------------------------------------------------------------------
    // Idempotency / shape preservation
    // --------------------------------------------------------------------

    [Fact]
    public async Task PersistAsync_Does_Not_Touch_CuratorMetadata_Field()
    {
        // CuratorMetadata is the curator-edited side of the handshake; the
        // auto-fill must NOT pre-populate that — only the AutoExtracted side.
        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: new[] { MakeDraft() });

        Assert.Null(_storedItems[0].CuratorMetadata);
    }

    [Fact]
    public async Task PersistAsync_Empty_Drafts_Returns_Empty_Without_Storing()
    {
        var ids = await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: Array.Empty<IngestionDraftQuestion>());

        Assert.Empty(ids);
        Assert.Empty(_storedItems);
        Assert.Empty(_storedPayloads);
    }

    [Fact]
    public async Task PersistAsync_Multi_Draft_Applies_Same_Inference_To_Every_Draft()
    {
        var drafts = new[] { MakeDraft(page: 1), MakeDraft(page: 2), MakeDraft(page: 3) };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        Assert.Equal(3, _storedItems.Count);
        Assert.Equal(3, _storedPayloads.Count);
        Assert.All(_storedItems, item =>
        {
            Assert.Equal("auto_extracted", item.MetadataState);
            Assert.Equal("5u",             item.AutoExtractedMetadata?.Track);
            Assert.Equal("math_5u",        item.AutoExtractedMetadata?.TaxonomyNode);
        });
    }
}
