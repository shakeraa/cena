// =============================================================================
// Cena Platform -- BagrutPdfIngestionService tests (Phase 2.3 / RDY-OCR-WIREUP-C)
//
// Verifies:
//   - Invokes cascade with Surface=AdminBatch + SourceType=BagrutReference
//   - Maps cascade blocks → ExtractedPage → draft questions
//   - Encrypted PDF → empty drafts + "encrypted_pdf" warning (no throw)
//   - Empty bytes / blank exam code → ArgumentException
//   - Low-confidence + CAS-failure counts surface in warnings
//   - Figures serialize to FigureSpecJson
//   - OcrInputException / OcrCircuitOpenException propagate
//
// NO STUBS in production. NSubstitute mocks the cascade in-test only.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutPdfIngestionServiceTests
{
    private readonly IOcrCascadeService _cascade = Substitute.For<IOcrCascadeService>();
    private readonly BagrutPdfIngestionService _service;

    public BagrutPdfIngestionServiceTests()
    {
        _service = new BagrutPdfIngestionService(
            _cascade, NullLogger<BagrutPdfIngestionService>.Instance);
    }

    private static OcrCascadeResult MakeResult(
        IReadOnlyList<OcrTextBlock>? text = null,
        IReadOnlyList<OcrMathBlock>? math = null,
        IReadOnlyList<OcrFigureRef>? figures = null,
        PdfTriageVerdict? triage = null,
        double confidence = 0.92,
        int casFailed = 0,
        bool humanReview = false,
        IReadOnlyList<string>? reasons = null,
        IReadOnlyList<string>? fallbacks = null) =>
        new(
            SchemaVersion: "1.0",
            Runner: "test",
            Source: "unit",
            Hints: null,
            PdfTriage: triage,
            TextBlocks: text ?? Array.Empty<OcrTextBlock>(),
            MathBlocks: math ?? Array.Empty<OcrMathBlock>(),
            Figures: figures ?? Array.Empty<OcrFigureRef>(),
            OverallConfidence: confidence,
            FallbacksFired: fallbacks ?? Array.Empty<string>(),
            CasValidatedMath: 0,
            CasFailedMath: casFailed,
            HumanReviewRequired: humanReview,
            ReasonsForReview: reasons ?? Array.Empty<string>(),
            LayerTimingsSeconds: new Dictionary<string, double>(),
            TotalLatencySeconds: 0.5,
            CapturedAt: "2026-04-17T00:00:00Z");

    private static byte[] MakePdfBytes(string marker = "unit")
    {
        // Content doesn't matter to the test (cascade is mocked); the
        // service only uses the bytes for hashing into the pdf_id.
        var bytes = System.Text.Encoding.UTF8.GetBytes($"%PDF-1.7\n{marker}");
        return bytes;
    }

    [Fact]
    public async Task IngestAsync_Happy_Path_Produces_Draft_Per_Populated_Page()
    {
        var text = new[]
        {
            new OcrTextBlock("שאלה 1: פתור את המשוואה x^2+2x=3",
                new BoundingBox(0, 0, 500, 100, Page: 1), Language.Hebrew, 0.92, IsRtl: true),
            new OcrTextBlock("Question 2: Prove the identity",
                new BoundingBox(0, 0, 500, 100, Page: 2), Language.English, 0.9, IsRtl: false),
        };
        var math = new[]
        {
            new OcrMathBlock("x^2 + 2x - 3 = 0",
                new BoundingBox(100, 100, 200, 50, Page: 1), 0.95, SympyParsed: true, CanonicalForm: "x**2+2*x-3"),
        };
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
                Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch, Arg.Any<CancellationToken>())
            .Returns(MakeResult(text: text, math: math));

        var result = await _service.IngestAsync(MakePdfBytes(), "math-5u-2023-winter", "curator@cena.dev");

        Assert.Equal("math-5u-2023-winter", result.ExamCode);
        Assert.StartsWith("pdf-", result.PdfId);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.QuestionsExtracted);
        Assert.Equal(0, result.FiguresExtracted);
        Assert.Contains(result.Drafts, d => d.SourcePage == 1 && d.LatexContent != null);
        Assert.Contains(result.Drafts, d => d.SourcePage == 2);
        // Every draft carries the bagrut reference-only review note (memory pointer)
        Assert.All(result.Drafts, d =>
            Assert.Contains(d.ReviewNotes, n => n.Contains("bagrut-reference")));
    }

    [Fact]
    public async Task IngestAsync_Uses_BagrutReference_Hint_And_AdminBatch_Surface()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult());

        await _service.IngestAsync(MakePdfBytes(), "exam-1", "curator@cena.dev");

        await _cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            "application/pdf",
            Arg.Is<OcrContextHints?>(h =>
                h != null &&
                h.Subject == "math" &&
                h.SourceType == SourceType.BagrutReference &&
                h.ExpectedFigures == true),
            CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_Encrypted_PDF_Returns_Empty_Drafts_With_Warning()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(triage: PdfTriageVerdict.Encrypted, humanReview: true));

        var result = await _service.IngestAsync(MakePdfBytes(), "exam-2", "curator@cena.dev");

        Assert.Empty(result.Drafts);
        Assert.Equal(0, result.TotalPages);
        Assert.Contains(result.Warnings, w => w.StartsWith("encrypted_pdf"));
    }

    [Fact]
    public async Task IngestAsync_Empty_Bytes_Throws_ArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.IngestAsync(Array.Empty<byte>(), "exam-3", "curator@cena.dev"));
    }

    [Fact]
    public async Task IngestAsync_Missing_ExamCode_Throws_ArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.IngestAsync(MakePdfBytes(), "   ", "curator@cena.dev"));
    }

    [Fact]
    public async Task IngestAsync_Low_Confidence_Surfaces_Warning_And_ReviewNote()
    {
        var text = new[]
        {
            new OcrTextBlock("fuzzy hebrew",
                new BoundingBox(0, 0, 500, 100, Page: 1), Language.Hebrew, 0.5, IsRtl: true),
        };
        var math = new[]
        {
            new OcrMathBlock("x = 1",
                new BoundingBox(0, 0, 100, 50, Page: 1), 0.55, SympyParsed: true, CanonicalForm: "x-1"),
        };
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(text: text, math: math, confidence: 0.55));

        var result = await _service.IngestAsync(MakePdfBytes(), "exam-4", "curator@cena.dev");

        Assert.Single(result.Drafts);
        Assert.InRange(result.Drafts[0].ExtractionConfidence, 0.0, 0.7);
        Assert.Contains(result.Drafts[0].ReviewNotes, n => n == "low-ocr-confidence");
        Assert.Contains(result.Warnings, w => w == "some_drafts_low_confidence");
    }

    [Fact]
    public async Task IngestAsync_Figures_Serialize_To_FigureSpecJson()
    {
        var text = new[]
        {
            new OcrTextBlock("with figure",
                new BoundingBox(0, 0, 500, 100, Page: 1), Language.English, 0.9, IsRtl: false),
        };
        var figures = new[]
        {
            new OcrFigureRef(
                new BoundingBox(50, 50, 200, 150, Page: 1),
                Kind: "diagram",
                CroppedPath: "/figs/p1-fig1.png",
                Caption: "Triangle ABC"),
        };
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(text: text, figures: figures));

        var result = await _service.IngestAsync(MakePdfBytes(), "exam-5", "curator@cena.dev");

        Assert.Equal(1, result.FiguresExtracted);
        var draft = Assert.Single(result.Drafts);
        Assert.NotNull(draft.FigureSpecJson);
        Assert.Contains("diagram", draft.FigureSpecJson);
        Assert.Contains("p1-fig1", draft.FigureSpecJson);
        Assert.Contains(draft.ReviewNotes, n => n.StartsWith("figures:"));
    }

    [Fact]
    public async Task IngestAsync_CasFailures_And_ReasonsForReview_Surface_In_Warnings()
    {
        var text = new[]
        {
            new OcrTextBlock("q1", new BoundingBox(0, 0, 100, 50, Page: 1),
                Language.English, 0.95, IsRtl: false),
        };
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(
                text: text,
                casFailed: 3,
                humanReview: true,
                reasons: new[] { "cas_mismatch", "low_math_confidence" },
                fallbacks: new[] { "mathpix" }));

        var result = await _service.IngestAsync(MakePdfBytes(), "exam-6", "curator@cena.dev");

        Assert.Contains(result.Warnings, w => w == "cas_failed:3");
        Assert.Contains(result.Warnings, w => w == "human_review_required");
        Assert.Contains(result.Warnings, w => w == "review:cas_mismatch");
        Assert.Contains(result.Warnings, w => w == "review:low_math_confidence");
        Assert.Contains(result.Warnings, w => w == "fallback_used:mathpix");
    }

    [Fact]
    public async Task IngestAsync_Propagates_OcrInputException()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Throws(new OcrInputException("malformed PDF"));

        await Assert.ThrowsAsync<OcrInputException>(() =>
            _service.IngestAsync(MakePdfBytes(), "exam-7", "curator@cena.dev"));
    }

    [Fact]
    public async Task IngestAsync_Propagates_OcrCircuitOpenException()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Throws(new OcrCircuitOpenException());

        await Assert.ThrowsAsync<OcrCircuitOpenException>(() =>
            _service.IngestAsync(MakePdfBytes(), "exam-8", "curator@cena.dev"));
    }
}
