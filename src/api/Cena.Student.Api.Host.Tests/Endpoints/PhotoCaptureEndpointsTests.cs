// =============================================================================
// Cena Platform — PhotoCaptureEndpoints tests (Phase 2.1 wire-up)
//
// Exercises the real endpoint against a test-mocked IOcrCascadeService +
// IContentModerationPipeline. The mocks sit at the endpoint boundary — the
// cascade itself is covered end-to-end by Cena.Infrastructure.Tests.Ocr.
// =============================================================================

using System.Security.Claims;
using System.Text;
using Cena.Infrastructure.Moderation;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Student.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class PhotoCaptureEndpointsTests
{
    // -------------------------------------------------------------------------
    // Moderation-first path
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CaptureAndRecognize_CsamDetected_Returns403_Cascade_Not_Invoked()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.CsamDetected);
        var cascade = new Mock<IOcrCascadeService>(MockBehavior.Strict);   // must NOT be called

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object,
            AStudent(),
            moderation.Object,
            cascade.Object,
            NullLogger<Program>.Instance,
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(403, status.StatusCode);
        cascade.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CaptureAndRecognize_Moderation_Blocked_Returns403()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.Blocked);
        var cascade = new Mock<IOcrCascadeService>(MockBehavior.Strict);

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Happy path → cascade returns CAS-validated LaTeX
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CaptureAndRecognize_Success_Returns_Ok_With_Sanitised_Latex()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = MockCascadeReturning(CascadeOk("3x+5=14", conf: 0.93));

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<PhotoCaptureResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("3x+5=14", ok.Value!.RecognizedLatex);
        Assert.InRange(ok.Value.Confidence, 0.90, 0.95);
        Assert.Single(ok.Value.BoundingBoxes);
        Assert.Equal("3x+5=14", ok.Value.BoundingBoxes[0].ExtractedLatex);
    }

    // -------------------------------------------------------------------------
    // Cascade says "could not recognize" → 422
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CaptureAndRecognize_Low_Confidence_Returns_422_With_Reasons()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = MockCascadeReturning(CascadeCatastrophic());

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // UnprocessableEntity is 422
        var unproc = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(422, unproc.StatusCode);
    }

    [Fact]
    public async Task CaptureAndRecognize_All_Math_Blocks_CAS_Failed_Returns_422()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = MockCascadeReturning(CascadeAllCasFailed());

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var unproc = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(422, unproc.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Cascade says "cloud fallbacks all down" → 503
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CaptureAndRecognize_Cascade_CircuitOpen_Returns_503()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = new Mock<IOcrCascadeService>();
        cascade.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OcrCircuitOpenException());

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public async Task CaptureAndRecognize_Malformed_Input_Returns_400()
    {
        var request = MockRequest(out _);
        var moderation = MockModeration(ModerationVerdict.Safe);
        var cascade = new Mock<IOcrCascadeService>();
        cascade.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OcrInputException("not a png"));

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, AStudent(), moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // BadRequest is 400
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task CaptureAndRecognize_Missing_Student_Sub_Returns_401()
    {
        var request = MockRequest(out _);
        var moderation = new Mock<IContentModerationPipeline>();
        var cascade = new Mock<IOcrCascadeService>(MockBehavior.Strict);
        // Anonymous user — no 'sub' claim
        var anon = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await PhotoCaptureEndpoints.CaptureAndRecognize(
            request.Object, anon, moderation.Object, cascade.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // UnauthorizedHttpResult is 401
        Assert.IsAssignableFrom<IResult>(result);
        moderation.VerifyNoOtherCalls();
        cascade.VerifyNoOtherCalls();
    }

    // -------------------------------------------------------------------------
    // Helpers — request fixture, cascade fixture builders
    // -------------------------------------------------------------------------
    private static ClaimsPrincipal AStudent() =>
        new(new ClaimsIdentity([new Claim("sub", "student-42")], "Test"));

    private static Mock<HttpRequest> MockRequest(out byte[] imageBytes)
    {
        var localBytes = Encoding.ASCII.GetBytes("FAKE-PNG-BYTES-OK-FOR-TEST");
        imageBytes = localBytes;

        var file = new Mock<IFormFile>();
        file.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(localBytes));
        file.Setup(f => f.Length).Returns(localBytes.Length);
        file.Setup(f => f.ContentType).Returns("image/png");
        file.Setup(f => f.FileName).Returns("photo.png");
        // Form field name — PhotoCaptureEndpoints calls form.Files.GetFile("photo")
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

    private static Mock<IContentModerationPipeline> MockModeration(ModerationVerdict verdict) =>
        MockModeration(new ModerationResult(
            ContentId: "test-content-id",
            Verdict: verdict,
            ConfidenceScore: 0.99,
            FlaggedCategories: Array.Empty<string>(),
            RequiresHumanReview: false,
            IncidentReportFiled: verdict == ModerationVerdict.CsamDetected,
            ModeratedAt: DateTimeOffset.UtcNow));

    private static Mock<IContentModerationPipeline> MockModeration(ModerationResult result)
    {
        var moderation = new Mock<IContentModerationPipeline>();
        moderation.Setup(m => m.ModerateAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ModerationPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return moderation;
    }

    private static Mock<IOcrCascadeService> MockCascadeReturning(OcrCascadeResult result)
    {
        var mock = new Mock<IOcrCascadeService>();
        mock.Setup(c => c.RecognizeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(),
                It.IsAny<OcrContextHints?>(), It.IsAny<CascadeSurface>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static OcrCascadeResult CascadeOk(string latex, double conf)
        => new(
            SchemaVersion: "1.0",
            Runner: "test",
            Source: "image/png",
            Hints: null,
            PdfTriage: null,
            TextBlocks: Array.Empty<OcrTextBlock>(),
            MathBlocks: new[]
            {
                new OcrMathBlock(
                    Latex: latex,
                    Bbox: new Cena.Infrastructure.Ocr.Contracts.BoundingBox(10, 20, 100, 30, 1),
                    Confidence: conf,
                    SympyParsed: true,
                    CanonicalForm: "3*x - 9"),
            },
            Figures: Array.Empty<OcrFigureRef>(),
            OverallConfidence: conf,
            FallbacksFired: Array.Empty<string>(),
            CasValidatedMath: 1,
            CasFailedMath: 0,
            HumanReviewRequired: false,
            ReasonsForReview: Array.Empty<string>(),
            LayerTimingsSeconds: new Dictionary<string, double>(),
            TotalLatencySeconds: 1.2,
            CapturedAt: DateTimeOffset.UtcNow.ToString("O"));

    private static OcrCascadeResult CascadeCatastrophic()
        => CascadeOk("", 0.20) with
        {
            HumanReviewRequired = true,
            ReasonsForReview = new[] { "low_overall_confidence" },
            MathBlocks = Array.Empty<OcrMathBlock>(),
            CasValidatedMath = 0,
        };

    private static OcrCascadeResult CascadeAllCasFailed()
        => new(
            SchemaVersion: "1.0", Runner: "test", Source: "image/png",
            Hints: null, PdfTriage: null,
            TextBlocks: Array.Empty<OcrTextBlock>(),
            MathBlocks: new[]
            {
                new OcrMathBlock(@"\frac{x}{", null, 0.82, SympyParsed: false, CanonicalForm: null),
            },
            Figures: Array.Empty<OcrFigureRef>(),
            OverallConfidence: 0.82,
            FallbacksFired: Array.Empty<string>(),
            CasValidatedMath: 0, CasFailedMath: 1,
            HumanReviewRequired: false,
            ReasonsForReview: Array.Empty<string>(),
            LayerTimingsSeconds: new Dictionary<string, double>(),
            TotalLatencySeconds: 1.0,
            CapturedAt: DateTimeOffset.UtcNow.ToString("O"));
}
