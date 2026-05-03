// =============================================================================
// Cena Platform — BagrutPdfIngestionService × vision-extractor integration
// (vision-extractor branch).
//
// Pins:
//   1. Flag ON + fake rasterizer + fake vision extractor returning canned
//      6-page result → ingestion produces 6 drafts with figure URLs in
//      FigureSpecJson, PNGs land on disk under the fake page dir, the
//      multi-layer cascade is NEVER called.
//   2. Flag OFF → vision seam ignored, cascade runs as before.
//   3. Flag ON but vision extractor returns null on page 2 → ingestion
//      falls back to the legacy cascade entirely (vision-path returns null;
//      caller picks up).
//   4. Flag ON but rasterizer raises an encrypted-PDF error → ingestion
//      returns an empty drafts list with the encrypted_pdf warning,
//      WITHOUT calling the cascade.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Segmenter;
using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Cena.Admin.Api.Tests.Ingestion.Vision;

public sealed class BagrutPdfIngestionServiceVisionPathTests : IDisposable
{
    private readonly string _scratchRoot;

    public BagrutPdfIngestionServiceVisionPathTests()
    {
        _scratchRoot = Path.Combine(
            Path.GetTempPath(),
            $"cena-vision-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_scratchRoot)) Directory.Delete(_scratchRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task FlagOn_VisionPathProduces6Drafts_CascadeNeverCalled()
    {
        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        var pagePaths = WriteSamplePages(6);
        var rasterizer = new FakeRasterizer(pagePaths);
        var visionExtractor = new FakeVisionExtractor(pageCount: 6);
        var cropper = new FakeCropper();
        var segmenter = new OneDraftPerPageSegmenter();

        var config = BuildConfig(visionEnabled: true);

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: config,
            rasterizer: rasterizer,
            visionExtractor: visionExtractor,
            figureCropper: cropper);

        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.7\nfake-vision-pdf");
        var result = await service.IngestAsync(pdfBytes, "math-5u-2024-summer", "curator@cena.dev");

        // 6 pages → 6 drafts (OneDraftPerPage on the vision-derived pages).
        Assert.Equal(6, result.TotalPages);
        Assert.Equal(6, result.QuestionsExtracted);
        Assert.Equal(6, result.Drafts.Count);

        // Each draft carries at least one figure from the canned extraction.
        foreach (var draft in result.Drafts)
        {
            Assert.NotNull(draft.FigureSpecJson);
            Assert.Contains("croppedPath", draft.FigureSpecJson!);
        }

        // Cascade was NOT called.
        await cascade.DidNotReceiveWithAnyArgs().RecognizeAsync(
            default, default!, default, default, default);

        // Rasterizer + vision-extractor + cropper all hit.
        Assert.Equal(1, rasterizer.CallCount);
        Assert.Equal(6, visionExtractor.CallCount);
        Assert.Equal(6, cropper.CallCount);
    }

    [Fact]
    public async Task FlagOff_VisionSeamIgnored_CascadeRunsAsBefore()
    {
        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        var pagePaths = WriteSamplePages(3);
        var rasterizer = new FakeRasterizer(pagePaths);
        var visionExtractor = new FakeVisionExtractor(pageCount: 3);
        var cropper = new FakeCropper();
        var segmenter = new OneDraftPerPageSegmenter();

        // Flag OFF — ingestion must use the cascade.
        var config = BuildConfig(visionEnabled: false);

        var ocr = BuildEmptyOcrResult();
        cascade.RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>())
            .Returns(ocr);

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: config,
            rasterizer: rasterizer,
            visionExtractor: visionExtractor,
            figureCropper: cropper);

        await service.IngestAsync(new byte[] { 1, 2, 3 }, "math-5u", "curator@cena.dev");

        await cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
        Assert.Equal(0, rasterizer.CallCount);
        Assert.Equal(0, visionExtractor.CallCount);
    }

    [Fact]
    public async Task FlagOn_VisionExtractorReturnsNull_FallsBackToCascade()
    {
        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        var pagePaths = WriteSamplePages(3);
        var rasterizer = new FakeRasterizer(pagePaths);
        // null on first call → vision path bails on page 1 and falls back.
        var visionExtractor = new FakeVisionExtractor(pageCount: 0)
        {
            ReturnNullOnPage = 1,
        };
        var cropper = new FakeCropper();
        var segmenter = new OneDraftPerPageSegmenter();
        var config = BuildConfig(visionEnabled: true);

        var ocr = BuildEmptyOcrResult();
        cascade.RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>())
            .Returns(ocr);

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: config,
            rasterizer: rasterizer,
            visionExtractor: visionExtractor,
            figureCropper: cropper);

        await service.IngestAsync(new byte[] { 1, 2, 3 }, "math-5u", "curator@cena.dev");

        // Vision extractor called once (page 1) → null → cascade kicks in.
        Assert.Equal(1, visionExtractor.CallCount);
        await cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlagOn_RasterizerRaisesEncrypted_ReturnsEmptyDrafts_WithWarning()
    {
        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        var rasterizer = new FakeRasterizer(Array.Empty<string>())
        {
            ThrowOnRasterize = new InvalidOperationException(
                "pdftoppm exit code 1: Command Line Error: Incorrect password"),
        };
        var visionExtractor = new FakeVisionExtractor(pageCount: 0);
        var cropper = new FakeCropper();
        var segmenter = new OneDraftPerPageSegmenter();
        var config = BuildConfig(visionEnabled: true);

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter,
            NullLogger<BagrutPdfIngestionService>.Instance,
            configuration: config,
            rasterizer: rasterizer,
            visionExtractor: visionExtractor,
            figureCropper: cropper);

        var result = await service.IngestAsync(
            new byte[] { 1, 2, 3 }, "math-5u", "curator@cena.dev");

        // Encrypted PDF surfaces as empty drafts + warning. Cascade NOT called.
        Assert.Equal(0, result.QuestionsExtracted);
        Assert.Empty(result.Drafts);
        Assert.Contains("encrypted_pdf", string.Join(",", result.Warnings));
        await cascade.DidNotReceiveWithAnyArgs().RecognizeAsync(
            default, default!, default, default, default);
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private IConfiguration BuildConfig(bool visionEnabled)
    {
        var settings = new Dictionary<string, string?>
        {
            [BagrutPdfIngestionService.VisionExtractorFlagKey] = visionEnabled ? "true" : "false",
        };
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private IReadOnlyList<string> WriteSamplePages(int count)
    {
        var pageDir = Path.Combine(_scratchRoot, "pages");
        Directory.CreateDirectory(pageDir);
        var paths = new List<string>(count);
        for (var i = 1; i <= count; i++)
        {
            var path = Path.Combine(pageDir, $"page-{i:D3}.png");
            using var img = new Image<Rgba32>(800, 1000, new Rgba32(255, 255, 255, 255));
            img.SaveAsPng(path, new PngEncoder());
            paths.Add(path);
        }
        return paths;
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

    // --------------------------------------------------------------------
    // Fakes
    // --------------------------------------------------------------------

    private sealed class FakeRasterizer : IPdfPageRasterizer
    {
        private readonly IReadOnlyList<string> _paths;
        public int CallCount { get; private set; }
        public Exception? ThrowOnRasterize { get; init; }

        public FakeRasterizer(IReadOnlyList<string> paths) { _paths = paths; }

        public Task<IReadOnlyList<string>> RasterizeAsync(
            byte[] pdfBytes, string pdfId, CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowOnRasterize is not null) throw ThrowOnRasterize;
            return Task.FromResult(_paths);
        }
    }

    private sealed class FakeVisionExtractor : IBagrutPageVisionExtractor
    {
        private readonly int _pageCount;
        public int CallCount { get; private set; }
        public int? ReturnNullOnPage { get; init; }

        public FakeVisionExtractor(int pageCount) { _pageCount = pageCount; }

        public Task<BagrutPageExtraction?> ExtractAsync(
            ReadOnlyMemory<byte> pagePngBytes, int pageNumber, string pdfId,
            CancellationToken ct = default)
        {
            CallCount++;
            if (ReturnNullOnPage == pageNumber) return Task.FromResult<BagrutPageExtraction?>(null);
            return Task.FromResult<BagrutPageExtraction?>(new BagrutPageExtraction(
                PromptText: $"Page {pageNumber} canned extraction — שאלה {pageNumber}",
                Latex: $"x_{pageNumber} = {pageNumber}",
                Figures: new[]
                {
                    new DetectedFigure(
                        X: 100 * pageNumber,
                        Y: 100,
                        Width: 200,
                        Height: 150,
                        Kind: "diagram",
                        AltText: $"figure-{pageNumber}"),
                },
                Confidence: 0.93));
        }
    }

    private sealed class FakeCropper : IFigureCropper
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<CroppedFigureRecord>> CropAsync(
            string pagePngPath, int pageNumber, string pdfId,
            IReadOnlyList<DetectedFigure> figures,
            CancellationToken ct = default)
        {
            CallCount++;
            var records = new List<CroppedFigureRecord>(figures.Count);
            for (var i = 0; i < figures.Count; i++)
            {
                var f = figures[i];
                records.Add(new CroppedFigureRecord(
                    PageNumber: pageNumber,
                    FigureIndex: i,
                    X: (int)f.X, Y: (int)f.Y,
                    Width: (int)f.Width, Height: (int)f.Height,
                    CroppedPath: $"/tmp/fake-page-{pageNumber}-fig-{i}.png",
                    Kind: "figure",
                    AltText: f.AltText));
            }
            return Task.FromResult<IReadOnlyList<CroppedFigureRecord>>(records);
        }
    }
}
