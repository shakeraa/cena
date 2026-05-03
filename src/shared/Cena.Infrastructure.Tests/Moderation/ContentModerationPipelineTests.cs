// =============================================================================
// RDY-001: Content Moderation Pipeline Tests
// Verifies fail-closed CSAM detection and AI safety fallback behavior.
// =============================================================================

using Cena.Infrastructure.Moderation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Infrastructure.Tests.Moderation;

public class ContentModerationPipelineTests
{
    private readonly IPhotoDnaClient _photoDna = Substitute.For<IPhotoDnaClient>();
    private readonly IContentSafetyClient _contentSafety = Substitute.For<IContentSafetyClient>();
    private readonly IIncidentReportService _incidentReporter = Substitute.For<IIncidentReportService>();
    private readonly ContentModerationPipeline _pipeline;

    public ContentModerationPipelineTests()
    {
        _pipeline = new ContentModerationPipeline(
            _photoDna,
            _contentSafety,
            _incidentReporter,
            NullLogger<ContentModerationPipeline>.Instance);
    }

    [Fact]
    public async Task ModerateAsync_CsamMatch_ReturnsCsamDetectedAndFilesIncident()
    {
        // Arrange
        var content = new byte[] { 0x01, 0x02 };
        _photoDna.CheckHashAsync(content, Arg.Any<CancellationToken>())
            .Returns(new PhotoDnaMatch(true, "match-123", "hash-abc"));

        // Act
        var result = await _pipeline.ModerateAsync(
            content, "image/jpeg", "student-1", new ModerationPolicy(), "1.2.3.4");

        // Assert
        Assert.Equal(ModerationVerdict.CsamDetected, result.Verdict);
        Assert.True(result.IncidentReportFiled);
        await _incidentReporter.Received(1).FileIncidentAsync(
            Arg.Any<string>(), "student-1", "1.2.3.4",
            Arg.Is<PhotoDnaMatch>(m => m.MatchId == "match-123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModerateAsync_PhotoDnaUnavailable_ReturnsBlocked_FailClosed()
    {
        // Arrange
        var content = new byte[] { 0x01, 0x02 };
        _photoDna.CheckHashAsync(content, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PhotoDnaMatch>(new PhotoDnaUnavailableException("service down")));

        // Act
        var result = await _pipeline.ModerateAsync(
            content, "image/jpeg", "student-1", new ModerationPolicy(), null);

        // Assert
        Assert.Equal(ModerationVerdict.Blocked, result.Verdict);
        Assert.Contains("csam_check_unavailable", result.FlaggedCategories);
        await _incidentReporter.DidNotReceive().FileIncidentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<PhotoDnaMatch>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModerateAsync_AiServiceUnavailable_ReturnsNeedsReview_NotSafe()
    {
        // Arrange
        var content = new byte[] { 0x01, 0x02 };
        _photoDna.CheckHashAsync(content, Arg.Any<CancellationToken>())
            .Returns(new PhotoDnaMatch(false, null, null));
        _contentSafety.ClassifyAsync(content, "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<double>(new HttpRequestException("AI service down")));

        // Act
        var result = await _pipeline.ModerateAsync(
            content, "image/jpeg", "student-1", new ModerationPolicy(), null);

        // Assert
        Assert.Equal(ModerationVerdict.NeedsReview, result.Verdict);
        Assert.True(result.RequiresHumanReview);
    }

    [Fact]
    public async Task ModerateAsync_HighSafetyScore_ReturnsSafe()
    {
        // Arrange
        var content = new byte[] { 0x01, 0x02 };
        _photoDna.CheckHashAsync(content, Arg.Any<CancellationToken>())
            .Returns(new PhotoDnaMatch(false, null, null));
        _contentSafety.ClassifyAsync(content, "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(0.98);

        // Act
        var result = await _pipeline.ModerateAsync(
            content, "image/jpeg", "student-1", new ModerationPolicy(), null);

        // Assert
        Assert.Equal(ModerationVerdict.Safe, result.Verdict);
        Assert.False(result.RequiresHumanReview);
    }

    [Fact]
    public async Task ModerateAsync_LowSafetyScore_ReturnsBlocked()
    {
        // Arrange
        var content = new byte[] { 0x01, 0x02 };
        _photoDna.CheckHashAsync(content, Arg.Any<CancellationToken>())
            .Returns(new PhotoDnaMatch(false, null, null));
        _contentSafety.ClassifyAsync(content, "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(0.10);

        // Act
        var result = await _pipeline.ModerateAsync(
            content, "image/jpeg", "student-1", new ModerationPolicy(), null);

        // Assert
        Assert.Equal(ModerationVerdict.Blocked, result.Verdict);
        Assert.Contains("low_safety_score", result.FlaggedCategories);
    }

    [Fact]
    public async Task ModerateAsync_MediumSafetyScore_ReturnsNeedsReview()
    {
        // Arrange
        var content = new byte[] { 0x01, 0x02 };
        _photoDna.CheckHashAsync(content, Arg.Any<CancellationToken>())
            .Returns(new PhotoDnaMatch(false, null, null));
        _contentSafety.ClassifyAsync(content, "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(0.50);

        // Act
        var result = await _pipeline.ModerateAsync(
            content, "image/jpeg", "student-1", new ModerationPolicy(), null);

        // Assert
        Assert.Equal(ModerationVerdict.NeedsReview, result.Verdict);
        Assert.True(result.RequiresHumanReview);
        Assert.Contains("uncertain", result.FlaggedCategories);
    }
}
