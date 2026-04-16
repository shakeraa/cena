// =============================================================================
// Cena Platform — PhotoUploadEndpoints tests (Phase 2.2 wire-up)
//
// Exercises the real endpoint against test-mocked IOcrCascadeService +
// IContentModerationPipeline. Covers:
//   - Magic-byte + content-type validation (image AND PDF paths)
//   - Moderation run on images; SKIPPED on PDFs by design
//   - CSAM/blocked → 403 for images
//   - OCR cascade invoked in both paths with correct SourceType hint
//   - Encrypted PDF → 422
//   - Low-confidence cascade → 422
//   - Happy path (PDF text shortcut) → 200 with ExtractedLatex
//   - OcrCircuitOpenException → 503
//
// The "old" PhotoUploadEndpointsTests.cs file remains excluded in the
// csproj — it was pre-Phase-2.2 and referenced missing HttpResults types.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Moderation;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Student.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class PhotoUploadEndpointsPhase2Tests
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47 };
    private static readonly byte[] PdfMagic = { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF, 0xE0 };

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Upload_With_Missing_File_Returns_400()
    {
        var req = MockRequestNoFile();
        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(),
            new Mock<IContentModerationPipeline>().Object,
            new Mock<IOcrCascadeService>().Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task Upload_With_Invalid_Content_Type_Returns_400()
    {
        var req = MockRequest("image/bmp", JpegMagic);   // bmp not in allow-list
        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(),
            new Mock<IContentModerationPipeline>().Object,
            new Mock<IOcrCascadeService>().Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task Upload_With_Mismatched_Magic_Bytes_Returns_400()
    {
        // Declared PNG, actual bytes are PDF — spoof attempt.
        var req = MockRequest("image/png", PdfMagic);
        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(),
            new Mock<IContentModerationPipeline>().Object,
            new Mock<IOcrCascadeService>().Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    // -------------------------------------------------------------------------
    // Moderation
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Image_Upload_CsamDetected_Returns_403_Cascade_Not_Invoked()
    {
        var req = MockRequest("image/png", PngMagic);
        var moderation = MockModeration(ModerationVerdict.CsamDetected);
        var cascade = new Mock<IOcrCascadeService>(MockBehavior.Strict);

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(403, status.StatusCode);
        cascade.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Image_Upload_Blocked_Returns_403()
    {
        var req = MockRequest("image/png", PngMagic);
        var moderation = MockModeration(ModerationVerdict.Blocked);
        var cascade = new Mock<IOcrCascadeService>(MockBehavior.Strict);

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Pdf_Upload_Skips_Moderation()
    {
        var req = MockRequest("application/pdf", PdfMagic);
        var moderation = new Mock<IContentModerationPipeline>(MockBehavior.Strict);  // MUST NOT be called
        var cascade = MockCascadeReturning(TextShortcutResult("3x+5=14"));

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsType<Ok<PhotoUploadResponse>>(result);
        moderation.VerifyNoOtherCalls();
    }

    // -------------------------------------------------------------------------
    // Cascade integration
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Image_Upload_Passes_StudentPhoto_Hint()
    {
        var req = MockRequest("image/png", PngMagic);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = new Mock<IOcrCascadeService>();
        OcrContextHints? captured = null;
        cascade.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, string, OcrContextHints?, CascadeSurface, CancellationToken>(
                (_, _, h, _, _) => captured = h)
            .ReturnsAsync(HappyOcrResult("x^2"));

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsType<Ok<PhotoUploadResponse>>(result);
        Assert.NotNull(captured);
        Assert.Equal(SourceType.StudentPhoto, captured!.SourceType);
    }

    [Fact]
    public async Task Pdf_Upload_Passes_StudentPdf_Hint()
    {
        var req = MockRequest("application/pdf", PdfMagic);
        var cascade = new Mock<IOcrCascadeService>();
        OcrContextHints? captured = null;
        cascade.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, string, OcrContextHints?, CascadeSurface, CancellationToken>(
                (_, _, h, _, _) => captured = h)
            .ReturnsAsync(TextShortcutResult("3x+5=14"));

        await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(),
            new Mock<IContentModerationPipeline>(MockBehavior.Strict).Object,
            cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(SourceType.StudentPdf, captured!.SourceType);
    }

    [Fact]
    public async Task Encrypted_Pdf_Returns_422()
    {
        var req = MockRequest("application/pdf", PdfMagic);
        var cascade = MockCascadeReturning(EncryptedPdfResult());

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(),
            new Mock<IContentModerationPipeline>(MockBehavior.Strict).Object,
            cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task Low_Confidence_Cascade_Returns_422()
    {
        var req = MockRequest("image/png", PngMagic);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = MockCascadeReturning(CatastrophicResult());

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task Cascade_CircuitOpen_Returns_503()
    {
        var req = MockRequest("image/png", PngMagic);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = new Mock<IOcrCascadeService>();
        cascade.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OcrCircuitOpenException());

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public async Task Success_Returns_Ok_With_ExtractedLatex_And_Status_processed_ocr()
    {
        var req = MockRequest("image/png", PngMagic);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = MockCascadeReturning(HappyOcrResult("x+1=2"));

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var ok = Assert.IsType<Ok<PhotoUploadResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("processed_ocr", ok.Value!.Status);
        Assert.Single(ok.Value.ExtractedLatex!, "x+1=2");
        Assert.True(ok.Value.ExifStripped);
        Assert.Null(ok.Value.PdfTriage);
    }

    [Fact]
    public async Task Pdf_Text_Shortcut_Returns_Status_processed_text_shortcut()
    {
        var req = MockRequest("application/pdf", PdfMagic);
        var cascade = MockCascadeReturning(TextShortcutResult("a=b"));

        var result = await PhotoUploadEndpoints.UploadPhoto(
            req.Object, AStudent(),
            new Mock<IContentModerationPipeline>(MockBehavior.Strict).Object,
            cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var ok = Assert.IsType<Ok<PhotoUploadResponse>>(result);
        Assert.Equal("processed_text_shortcut", ok.Value!.Status);
        Assert.Equal("text", ok.Value.PdfTriage);
        Assert.False(ok.Value.ExifStripped);   // PDFs: no EXIF path
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static ClaimsPrincipal AStudent() =>
        new(new ClaimsIdentity([new Claim("sub", "student-42")], "Test"));

    private static Mock<HttpRequest> MockRequest(string contentType, byte[] magic)
    {
        var file = new Mock<IFormFile>();
        file.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(magic));
        file.Setup(f => f.Length).Returns(magic.Length);
        file.Setup(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.FileName).Returns("file" + (contentType == "application/pdf" ? ".pdf" : ".png"));
        file.Setup(f => f.Name).Returns("photo");

        var files = new FormFileCollection { file.Object };
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);

        var req = new Mock<HttpRequest>();
        req.SetupGet(r => r.HasFormContentType).Returns(true);
        req.Setup(r => r.ReadFormAsync(It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var connection = new Mock<ConnectionInfo>();
        connection.SetupGet(c => c.RemoteIpAddress).Returns(System.Net.IPAddress.Loopback);
        var httpCtx = new Mock<HttpContext>();
        httpCtx.SetupGet(c => c.Connection).Returns(connection.Object);
        req.SetupGet(r => r.HttpContext).Returns(httpCtx.Object);
        return req;
    }

    private static Mock<HttpRequest> MockRequestNoFile()
    {
        var files = new FormFileCollection();
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);
        var req = new Mock<HttpRequest>();
        req.SetupGet(r => r.HasFormContentType).Returns(true);
        req.Setup(r => r.ReadFormAsync(It.IsAny<CancellationToken>())).ReturnsAsync(form);
        return req;
    }

    private static Mock<IContentModerationPipeline> MockModeration(ModerationVerdict verdict)
    {
        var mock = new Mock<IContentModerationPipeline>();
        mock.Setup(m => m.ModerateAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ModerationPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationResult(
                ContentId: "test-hash",
                Verdict: verdict,
                ConfidenceScore: 0.99,
                FlaggedCategories: Array.Empty<string>(),
                RequiresHumanReview: false,
                IncidentReportFiled: verdict == ModerationVerdict.CsamDetected,
                ModeratedAt: DateTimeOffset.UtcNow));
        return mock;
    }

    private static Mock<IOcrCascadeService> MockCascadeReturning(OcrCascadeResult r)
    {
        var mock = new Mock<IOcrCascadeService>();
        mock.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(r);
        return mock;
    }

    private static OcrCascadeResult HappyOcrResult(string latex) => new(
        SchemaVersion: "1.0", Runner: "test", Source: "image/png",
        Hints: null, PdfTriage: null,
        TextBlocks: Array.Empty<OcrTextBlock>(),
        MathBlocks: new[]
        {
            new OcrMathBlock(latex, new Cena.Infrastructure.Ocr.Contracts.BoundingBox(0, 0, 100, 30, 1),
                0.92, SympyParsed: true, CanonicalForm: "canonical"),
        },
        Figures: Array.Empty<OcrFigureRef>(),
        OverallConfidence: 0.92, FallbacksFired: Array.Empty<string>(),
        CasValidatedMath: 1, CasFailedMath: 0,
        HumanReviewRequired: false, ReasonsForReview: Array.Empty<string>(),
        LayerTimingsSeconds: new Dictionary<string, double>(),
        TotalLatencySeconds: 1.0,
        CapturedAt: DateTimeOffset.UtcNow.ToString("O"));

    private static OcrCascadeResult TextShortcutResult(string latex) =>
        HappyOcrResult(latex) with { PdfTriage = PdfTriageVerdict.Text };

    private static OcrCascadeResult EncryptedPdfResult() => new(
        SchemaVersion: "1.0", Runner: "test", Source: "application/pdf",
        Hints: null, PdfTriage: PdfTriageVerdict.Encrypted,
        TextBlocks: Array.Empty<OcrTextBlock>(),
        MathBlocks: Array.Empty<OcrMathBlock>(),
        Figures: Array.Empty<OcrFigureRef>(),
        OverallConfidence: 0.0, FallbacksFired: Array.Empty<string>(),
        CasValidatedMath: 0, CasFailedMath: 0,
        HumanReviewRequired: true,
        ReasonsForReview: new[] { "preprocess_failed_or_encrypted" },
        LayerTimingsSeconds: new Dictionary<string, double>(),
        TotalLatencySeconds: 0.1,
        CapturedAt: DateTimeOffset.UtcNow.ToString("O"));

    private static OcrCascadeResult CatastrophicResult() =>
        HappyOcrResult("") with
        {
            OverallConfidence = 0.2,
            HumanReviewRequired = true,
            ReasonsForReview = new[] { "low_overall_confidence" },
            MathBlocks = Array.Empty<OcrMathBlock>(),
            CasValidatedMath = 0,
        };
}
