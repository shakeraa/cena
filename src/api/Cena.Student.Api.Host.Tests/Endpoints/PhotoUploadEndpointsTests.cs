// =============================================================================
// RDY-001: Photo Upload Endpoint Tests
// Verifies moderation integration and fail-closed behavior.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Moderation;
using Cena.Student.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class PhotoUploadEndpointsTests
{
    [Fact]
    public async Task UploadPhoto_CsamDetected_Returns403()
    {
        // Arrange
        var file = CreateMockFormFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0]);
        var request = CreateMockRequest(file);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "student-1")], "Test"));
        var moderation = new Mock<IContentModerationPipeline>();
        moderation.Setup(m => m.ModerateAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ModerationPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationResult(
                "content-1", ModerationVerdict.CsamDetected, 1.0,
                ["csam"], false, true, DateTimeOffset.UtcNow));

        // Act
        var result = await PhotoUploadEndpoints.UploadPhoto(
            request.Object, user, moderation.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_ModerationBlocked_Returns403()
    {
        // Arrange
        var file = CreateMockFormFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0]);
        var request = CreateMockRequest(file);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "student-1")], "Test"));
        var moderation = new Mock<IContentModerationPipeline>();
        moderation.Setup(m => m.ModerateAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ModerationPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationResult(
                "content-1", ModerationVerdict.Blocked, 0.10,
                ["low_safety_score"], false, false, DateTimeOffset.UtcNow));

        // Act
        var result = await PhotoUploadEndpoints.UploadPhoto(
            request.Object, user, moderation.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_ModerationNeedsReview_ReturnsQueuedForReview()
    {
        // Arrange
        var file = CreateMockFormFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0]);
        var request = CreateMockRequest(file);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "student-1")], "Test"));
        var moderation = new Mock<IContentModerationPipeline>();
        moderation.Setup(m => m.ModerateAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ModerationPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationResult(
                "content-1", ModerationVerdict.NeedsReview, 0.50,
                ["uncertain"], true, false, DateTimeOffset.UtcNow));

        // Act
        var result = await PhotoUploadEndpoints.UploadPhoto(
            request.Object, user, moderation.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Ok<PhotoUploadResponse>>(result);
        Assert.Equal("queued_for_review", okResult.Value!.Status);
        Assert.Equal("NeedsReview", okResult.Value.ModerationVerdict);
    }

    [Fact]
    public async Task UploadPhoto_ModerationSafe_ReturnsQueuedForProcessing()
    {
        // Arrange
        var file = CreateMockFormFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0]);
        var request = CreateMockRequest(file);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "student-1")], "Test"));
        var moderation = new Mock<IContentModerationPipeline>();
        moderation.Setup(m => m.ModerateAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ModerationPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationResult(
                "content-1", ModerationVerdict.Safe, 0.98,
                [], false, false, DateTimeOffset.UtcNow));

        // Act
        var result = await PhotoUploadEndpoints.UploadPhoto(
            request.Object, user, moderation.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Ok<PhotoUploadResponse>>(result);
        Assert.Equal("queued_for_processing", okResult.Value!.Status);
        Assert.Equal("Safe", okResult.Value.ModerationVerdict);
    }

    [Fact]
    public async Task UploadPhoto_InvalidMagicBytes_Returns400()
    {
        // Arrange
        var file = CreateMockFormFile("image/jpeg", [0x00, 0x00, 0x00, 0x00]);
        var request = CreateMockRequest(file);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "student-1")], "Test"));
        var moderation = new Mock<IContentModerationPipeline>();

        // Act
        var result = await PhotoUploadEndpoints.UploadPhoto(
            request.Object, user, moderation.Object,
            NullLogger<Program>.Instance, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequest<object>>(result);
        Assert.Equal("File content does not match declared content type", badRequest.Value!.GetType().GetProperty("error")!.GetValue(badRequest.Value));
    }

    private static Mock<HttpRequest> CreateMockRequest(IFormFile file)
    {
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection { file });

        var request = new Mock<HttpRequest>();
        request.Setup(r => r.HasFormContentType).Returns(true);
        request.Setup(r => r.ReadFormAsync(It.IsAny<CancellationToken>())).ReturnsAsync(form);
        request.Setup(r => r.HttpContext).Returns(new DefaultHttpContext());
        return request;
    }

    private static IFormFile CreateMockFormFile(string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "photo", "photo.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
