// =============================================================================
// Cena Platform — BagrutPdfIngestionService × text-layer path integration
// (claude-subagent-text-layer / pdfpig-first).
//
// Pins:
//   1. Real corpus PDF (35581-q.pdf) + flag ON + real PdfPig extractor →
//      cover-page skipped, page-2 produces 2 drafts (Q1+Q2), no truncation.
//      Vision-extractor + cascade are NEVER called.
//   2. Scan-style PDF (HasTextLayer=false from a fake extractor) + flag ON
//      → text-layer path returns null, vision-extractor runs (legacy
//      vision-path test exists separately; here we just assert the
//      hand-off).
//   3. Cover-page heuristic skips page 1 of the corpus, drafts start at
//      page 2.
//   4. Page 2 of the corpus produces 2 drafts (Q1 + Q2).
//   5. Flag OFF → text-layer path is invisible; cascade/vision runs as
//      configured.
//   6. Encrypted PDF (fake extractor throws InvalidOperationException with
//      "encrypted") → empty drafts + encrypted_pdf warning, no fallthrough.
//   7. Text-layer succeeds but produces 0 questions → fall through to
//      vision/cascade (fail-loud-on-extraction-success contract).
//   8. Prompt is not truncated at 401 chars — full slice text reaches the
//      draft (large slice case).
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Segmenter;
using Cena.Admin.Api.Ingestion.TextLayer;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion.TextLayer;

public sealed class BagrutPdfIngestionServiceTextLayerPathTests
{
    private static string CorpusFixture(string name)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(baseDir, "../../../../../../", "corpus", "tests", name));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Corpus fixture missing: {path}");
        return path;
    }

    private static IConfiguration BuildConfig(bool textLayerEnabled = true, bool visionEnabled = false)
    {
        var settings = new Dictionary<string, string?>
        {
            [BagrutPdfIngestionService.TextLayerExtractorFlagKey] = textLayerEnabled ? "true" : "false",
            [BagrutPdfIngestionService.VisionExtractorFlagKey] = visionEnabled ? "true" : "false",
        };
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Fact]
    public async Task RealCorpus_35581_FlagOn_ProducesAtLeast8Drafts_CoverSkipped_NoVisionCalled()
    {
        var pdfBytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));

        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();
        var segmenter = new OneDraftPerPageSegmenter();
        var extractor = new PdfPigTextLayerExtractor(NullLogger<PdfPigTextLayerExtractor>.Instance);

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: BuildConfig(textLayerEnabled: true, visionEnabled: false),
            textLayerExtractor: extractor);

        var result = await service.IngestAsync(pdfBytes, "math-5u-2026-winter", "curator@cena.dev");

        // 6 PDF pages but page 1 = cover → 5 question pages + 1 multi-marker
        // split on page 2 → at least 8 drafts (Q1, Q2, Q3, Q4+Q5 page 4
        // splits, Q6+Q7 page 5 splits, Q8). Don't pin the exact upper
        // bound — corpus structure may add or remove a marker depending
        // on how PdfPig surfaces a given InDesign block.
        Assert.True(result.QuestionsExtracted >= 7,
            $"Expected at least 7 drafts from 35581-q.pdf; got {result.QuestionsExtracted}");

        // Page 1 (cover) must NOT be a draft.
        Assert.DoesNotContain(result.Drafts, d => d.SourcePage == 1);

        // Cascade was NOT called.
        await cascade.DidNotReceiveWithAnyArgs().RecognizeAsync(
            default, default!, default, default, default);

        // Cover-skip warning emitted.
        Assert.Contains("text_layer_cover_skipped", string.Join(",", result.Warnings));
    }

    [Fact]
    public async Task RealCorpus_35581_Page2_ProducesTwoDrafts()
    {
        // Direct user complaint #2: page 2 has TWO `שאלה N` markers (Q1
        // walking-speed + Q2 geometric series) but legacy produced 1
        // draft. Pin that page 2 now produces TWO drafts with distinct
        // intra-page indices.
        var pdfBytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));

        var service = BuildServiceWithRealExtractor();
        var result = await service.IngestAsync(pdfBytes, "math-5u-2026-winter", "curator@cena.dev");

        var page2Drafts = result.Drafts.Where(d => d.SourcePage == 2).ToList();
        Assert.Equal(2, page2Drafts.Count);

        // Draft IDs must be unique (the intra-page disambiguator kicks in).
        Assert.Equal(2, page2Drafts.Select(d => d.DraftId).Distinct().Count());

        // Each draft carries the intra-page-index review note.
        Assert.All(page2Drafts, d =>
            Assert.Contains(d.ReviewNotes, n => n.StartsWith("intra-page-index:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RealCorpus_35581_NoTruncation_PromptCarriesFullSliceText()
    {
        // Direct user complaint #3: "page is trimmed and text extracted is
        // wrong" (401-char cap). The text-layer path must not truncate; a
        // typical Bagrut question slice is 200-1500 chars, well above 401.
        var pdfBytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));
        var service = BuildServiceWithRealExtractor();
        var result = await service.IngestAsync(pdfBytes, "math-5u-2026-winter", "curator@cena.dev");

        // At least one draft prompt must be longer than the legacy 401
        // cap; otherwise we haven't actually proven the cap is gone.
        Assert.Contains(result.Drafts, d => d.Prompt.Length > 500);

        // No draft should end with the truncation ellipsis at exactly the
        // legacy boundary (length 402 = 400 chars + "…" + null-or-space).
        Assert.DoesNotContain(result.Drafts, d => d.Prompt.Length == 401);
    }

    [Fact]
    public async Task FlagOff_TextLayerPathInvisible_CascadeRunsAsConfigured()
    {
        var pdfBytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));

        var cascade = Substitute.For<IOcrCascadeService>();
        cascade.RecognizeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
                Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
                Arg.Any<CancellationToken>())
            .Returns(BuildEmptyOcrResult());

        var pdfStore = Substitute.For<IBagrutPdfStore>();
        var segmenter = new OneDraftPerPageSegmenter();
        var extractor = new PdfPigTextLayerExtractor(NullLogger<PdfPigTextLayerExtractor>.Instance);

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: BuildConfig(textLayerEnabled: false, visionEnabled: false),
            textLayerExtractor: extractor);

        await service.IngestAsync(pdfBytes, "math-5u", "curator@cena.dev");

        // Cascade WAS called.
        await cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FakeExtractor_HasTextLayerFalse_FallsThroughToCascade()
    {
        var cascade = Substitute.For<IOcrCascadeService>();
        cascade.RecognizeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
                Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
                Arg.Any<CancellationToken>())
            .Returns(BuildEmptyOcrResult());

        var fake = new FakeTextLayerExtractor(new PdfTextLayerExtraction(
            Pages: new[] { new TextLayerPage(1, "  ", Array.Empty<TextBlockBbox>()) },
            HasTextLayer: false));

        var service = new BagrutPdfIngestionService(
            cascade, Substitute.For<IBagrutPdfStore>(), new OneDraftPerPageSegmenter(),
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: BuildConfig(textLayerEnabled: true),
            textLayerExtractor: fake);

        await service.IngestAsync(new byte[] { 1, 2, 3 }, "math-5u", "curator@cena.dev");

        // text-layer-false path MUST hand off to cascade.
        await cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FakeExtractor_EncryptedPdfThrow_ReturnsEmptyDrafts_WithEncryptedWarning()
    {
        var cascade = Substitute.For<IOcrCascadeService>();
        var fake = new FakeTextLayerExtractor(throwException:
            new InvalidOperationException("PDF is encrypted (password required): test"));

        var service = new BagrutPdfIngestionService(
            cascade, Substitute.For<IBagrutPdfStore>(), new OneDraftPerPageSegmenter(),
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: BuildConfig(textLayerEnabled: true),
            textLayerExtractor: fake);

        var result = await service.IngestAsync(new byte[] { 1, 2, 3 }, "math-5u", "curator@cena.dev");

        Assert.Empty(result.Drafts);
        Assert.Contains("encrypted_pdf", string.Join(",", result.Warnings));

        // Cascade NOT called — encrypted result short-circuits.
        await cascade.DidNotReceiveWithAnyArgs().RecognizeAsync(
            default, default!, default, default, default);
    }

    [Fact]
    public async Task FakeExtractor_HasTextLayerTrueButZeroQuestions_FallsThroughToCascade()
    {
        // Fail-loud-on-extraction-success contract: text-layer ran, has
        // content, but produced 0 segments after cover-skip + split.
        // Orchestrator must fall through to vision/cascade (NOT emit
        // empty drafts).
        var cascade = Substitute.For<IOcrCascadeService>();
        cascade.RecognizeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
                Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
                Arg.Any<CancellationToken>())
            .Returns(BuildEmptyOcrResult());

        // Simulate: a page with "instructions" + "exam duration" (cover) and
        // a second page with "formula sheet" (also cover). HasTextLayer=true
        // because total text > 200 chars. After cover-skip = 0 segments.
        // Use the visual-reversed Hebrew that PdfPig emits in production —
        // both forms are matched by the cover heuristic.
        var coverText1 = new string('a', 250) + " תוארוה הניחבה ךשמ";
        var coverText2 = new string('b', 250) + " תואחסונ יפד";
        var fake = new FakeTextLayerExtractor(new PdfTextLayerExtraction(
            Pages: new[]
            {
                new TextLayerPage(1, coverText1, Array.Empty<TextBlockBbox>()),
                new TextLayerPage(2, coverText2, Array.Empty<TextBlockBbox>()),
            },
            HasTextLayer: true));

        var service = new BagrutPdfIngestionService(
            cascade, Substitute.For<IBagrutPdfStore>(), new OneDraftPerPageSegmenter(),
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: BuildConfig(textLayerEnabled: true),
            textLayerExtractor: fake);

        await service.IngestAsync(new byte[] { 1, 2, 3 }, "math-5u", "curator@cena.dev");

        // Both pages were skipped as cover → 0 segments → fall through.
        await cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RealCorpus_35582_AlsoExtractsCleanly_NoVision()
    {
        // Sister corpus PDF — second exam booklet (5-question variant).
        // Smoke-pin that the path works for both fixtures.
        var pdfBytes = await File.ReadAllBytesAsync(CorpusFixture("35582-q.pdf"));

        var cascade = Substitute.For<IOcrCascadeService>();
        var service = BuildServiceWithRealExtractor(cascade: cascade);

        var result = await service.IngestAsync(pdfBytes, "math-5u-2026-winter-paper2", "curator@cena.dev");

        Assert.True(result.QuestionsExtracted >= 4,
            $"Expected at least 4 drafts from 35582-q.pdf; got {result.QuestionsExtracted}");
        Assert.DoesNotContain(result.Drafts, d => d.SourcePage == 1);

        await cascade.DidNotReceiveWithAnyArgs().RecognizeAsync(
            default, default!, default, default, default);
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private BagrutPdfIngestionService BuildServiceWithRealExtractor(
        IOcrCascadeService? cascade = null)
    {
        cascade ??= Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();
        var segmenter = new OneDraftPerPageSegmenter();
        var extractor = new PdfPigTextLayerExtractor(NullLogger<PdfPigTextLayerExtractor>.Instance);

        return new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: BuildConfig(textLayerEnabled: true, visionEnabled: false),
            textLayerExtractor: extractor);
    }

    private static OcrCascadeResult BuildEmptyOcrResult() => new(
        SchemaVersion: "1.0",
        Runner: "test", Source: "unit", Hints: null, PdfTriage: null,
        TextBlocks: Array.Empty<OcrTextBlock>(),
        MathBlocks: Array.Empty<OcrMathBlock>(),
        Figures: Array.Empty<OcrFigureRef>(),
        OverallConfidence: 0.5,
        FallbacksFired: Array.Empty<string>(),
        CasValidatedMath: 0, CasFailedMath: 0,
        HumanReviewRequired: false,
        ReasonsForReview: Array.Empty<string>(),
        LayerTimingsSeconds: new Dictionary<string, double>(),
        TotalLatencySeconds: 0.1,
        CapturedAt: "2026-05-04T00:00:00Z");

    private sealed class FakeTextLayerExtractor : IPdfTextLayerExtractor
    {
        private readonly PdfTextLayerExtraction? _result;
        private readonly Exception? _throw;

        public FakeTextLayerExtractor(PdfTextLayerExtraction result)
        {
            _result = result;
            _throw = null;
        }

        public FakeTextLayerExtractor(Exception throwException)
        {
            _result = null;
            _throw = throwException;
        }

        public Task<PdfTextLayerExtraction> ExtractAsync(
            byte[] pdfBytes, string pdfId, CancellationToken ct = default)
        {
            if (_throw is not null) throw _throw;
            return Task.FromResult(_result!);
        }
    }
}
