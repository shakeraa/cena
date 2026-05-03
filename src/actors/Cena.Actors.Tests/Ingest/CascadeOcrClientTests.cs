// =============================================================================
// Cena Platform -- CascadeOcrClient tests (Phase 2.3 / RDY-OCR-WIREUP-C)
//
// Verifies the adapter correctly:
//   - Delegates to IOcrCascadeService with Surface=AdminBatch
//   - Maps OcrCascadeResult → legacy OcrPageOutput/OcrDocumentOutput
//   - Translates Language enum to the "he"/"en"/"ar" strings the legacy
//     consumers expect
//   - Picks the best CAS-validated math block for ExtractLatexAsync
//   - Propagates OcrInputException and OcrCircuitOpenException
// =============================================================================
// NO STUBS in production. NSubstitute mocks the cascade for isolation.

using Cena.Actors.Ingest;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

// OcrTextBlock exists in both Cena.Infrastructure.Ocr.Contracts (cascade side)
// and Cena.Actors.Ingest (legacy side). Alias the Infrastructure one so the
// test text reads naturally; the legacy type stays namespace-qualified.
using InfraTextBlock = Cena.Infrastructure.Ocr.Contracts.OcrTextBlock;

namespace Cena.Actors.Tests.Ingest;

public sealed class CascadeOcrClientTests
{
    private readonly IOcrCascadeService _cascade = Substitute.For<IOcrCascadeService>();
    private readonly CascadeOcrClient _client;

    public CascadeOcrClientTests()
    {
        _client = new CascadeOcrClient(_cascade, NullLogger<CascadeOcrClient>.Instance);
    }

    private static OcrCascadeResult Result(
        IReadOnlyList<InfraTextBlock>? text = null,
        IReadOnlyList<OcrMathBlock>? math = null,
        double confidence = 0.9,
        PdfTriageVerdict? triage = null,
        IReadOnlyList<string>? fallbacks = null,
        bool humanReview = false) =>
        new(
            SchemaVersion: "1.0",
            Runner: "test",
            Source: "unit",
            Hints: null,
            PdfTriage: triage,
            TextBlocks: text ?? Array.Empty<InfraTextBlock>(),
            MathBlocks: math ?? Array.Empty<OcrMathBlock>(),
            Figures: Array.Empty<OcrFigureRef>(),
            OverallConfidence: confidence,
            FallbacksFired: fallbacks ?? Array.Empty<string>(),
            CasValidatedMath: 0,
            CasFailedMath: 0,
            HumanReviewRequired: humanReview,
            ReasonsForReview: Array.Empty<string>(),
            LayerTimingsSeconds: new Dictionary<string, double>(),
            TotalLatencySeconds: 0.25,
            CapturedAt: "2026-04-17T00:00:00Z");

    [Fact]
    public async Task ProcessPageAsync_Maps_Text_And_Math_To_Legacy_Output()
    {
        // Arrange
        var text = new[]
        {
            new InfraTextBlock("שאלה 1: פתור את המשוואה", null, Language.Hebrew, 0.91, IsRtl: true),
            new InfraTextBlock("Find x such that", null, Language.English, 0.88, IsRtl: false),
        };
        var math = new[]
        {
            new OcrMathBlock("x^2 + 2x - 3 = 0", null, 0.95, SympyParsed: true, CanonicalForm: "x**2+2*x-3"),
            new OcrMathBlock("\\frac{x}{2}",       null, 0.8,  SympyParsed: true, CanonicalForm: "x/2"),
        };
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), "image/png",
                Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch, Arg.Any<CancellationToken>())
            .Returns(Result(text: text, math: math, confidence: 0.92));

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        // Act
        var page = await _client.ProcessPageAsync(stream, "image/png");

        // Assert
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("he", page.DetectedLanguage);   // majority
        Assert.InRange(page.Confidence, 0.91f, 0.93f);
        Assert.Contains("פתור", page.RawText);
        Assert.Equal(2, page.MathExpressions.Count);
        Assert.Equal("x^2 + 2x - 3 = 0", page.MathExpressions["eq_1"]);
        Assert.Equal("\\frac{x}{2}", page.MathExpressions["eq_2"]);
        // TextBlocks include text + math blocks
        Assert.Equal(4, page.TextBlocks.Count);
        Assert.Contains(page.TextBlocks, b => b.IsMath && b.Text.Contains("x^2"));
    }

    [Fact]
    public async Task ProcessPageAsync_Uses_AdminBatch_Surface_And_AdminUpload_Hint()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(Result());

        using var stream = new MemoryStream(new byte[] { 1 });
        await _client.ProcessPageAsync(stream, "image/png");

        await _cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            "image/png",
            Arg.Is<OcrContextHints?>(h =>
                h != null &&
                h.Subject == "math" &&
                h.SourceType == SourceType.AdminUpload),
            CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDocumentAsync_Passes_Pdf_ContentType()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(Result(fallbacks: new[] { "mathpix" }, confidence: 0.85));

        using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var doc = await _client.ProcessDocumentAsync(stream);

        await _cascade.Received(1).RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            "application/pdf",
            Arg.Any<OcrContextHints?>(),
            CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>());

        Assert.Single(doc.Pages);
        Assert.Equal(1, doc.PageCount);
        // Mathpix fallback → ~$0.0003/page cost estimate
        Assert.True(doc.EstimatedCostUsd > 0m, "Cost should be non-zero when a cloud fallback fired");
    }

    [Fact]
    public async Task ProcessDocumentAsync_Encrypted_PDF_Returns_Zero_PageCount()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(Result(triage: PdfTriageVerdict.Encrypted, humanReview: true));

        using var stream = new MemoryStream(new byte[] { 1 });
        var doc = await _client.ProcessDocumentAsync(stream);

        Assert.Equal(0, doc.PageCount);
    }

    [Fact]
    public async Task ExtractLatexAsync_Picks_Highest_Conf_Sympy_Parsed_Block()
    {
        var math = new[]
        {
            new OcrMathBlock("\\alpha",           null, 0.99, SympyParsed: false, CanonicalForm: null), // highest conf but not parsed
            new OcrMathBlock("x^2 + 2x - 3 = 0",  null, 0.90, SympyParsed: true,  CanonicalForm: "x**2+2*x-3"),
            new OcrMathBlock("y = mx + b",        null, 0.70, SympyParsed: true,  CanonicalForm: "y-m*x-b"),
        };
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(Result(math: math));

        using var stream = new MemoryStream(new byte[] { 1 });
        var latex = await _client.ExtractLatexAsync(stream);

        Assert.Equal("x^2 + 2x - 3 = 0", latex);
    }

    [Fact]
    public async Task ExtractLatexAsync_No_Math_Returns_Empty_String()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Returns(Result());

        using var stream = new MemoryStream(new byte[] { 1 });
        var latex = await _client.ExtractLatexAsync(stream);

        Assert.Equal(string.Empty, latex);
    }

    [Fact]
    public async Task ProcessPageAsync_Propagates_OcrInputException()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Throws(new OcrInputException("empty bytes"));

        using var stream = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<OcrInputException>(() =>
            _client.ProcessPageAsync(stream, "image/png"));
    }

    [Fact]
    public async Task ProcessDocumentAsync_Propagates_OcrCircuitOpenException()
    {
        _cascade
            .RecognizeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(),
                Arg.Any<OcrContextHints?>(), Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>())
            .Throws(new OcrCircuitOpenException());

        using var stream = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<OcrCircuitOpenException>(() =>
            _client.ProcessDocumentAsync(stream));
    }

    [Fact]
    public void ProviderName_Identifies_Cascade()
    {
        Assert.Equal("cena-ocr-cascade-v1", _client.ProviderName);
    }
}
