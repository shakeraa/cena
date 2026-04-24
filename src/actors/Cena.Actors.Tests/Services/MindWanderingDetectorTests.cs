using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-004: Mind-Wandering Detector tests.
/// Covers aware drift (gap → recovery), unaware drift (gradual increase),
/// focused state, and ambiguous patterns.
/// </summary>
public sealed class MindWanderingDetectorTests
{
    private readonly MindWanderingDetector _detector = new();

    [Fact]
    public void AwareDrift_DetectedFromRtGapThenRecovery()
    {
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2100, 8500, 2200, 1900], // gap at index 2, then normal
            BaselineRtMs: [2000, 2100, 1900, 2050, 2000],
            RecentAccuracies: [1, 1, 0, 1, 1]
        );
        var result = _detector.Detect(input);
        Assert.Equal(MindWanderingState.AwareDrift, result.State);
        Assert.Equal(2, result.GapIndex); // gap was at index 2
        Assert.True(result.Confidence >= 0.6);
    }

    [Fact]
    public void UnawareDrift_DetectedFromGradualVarianceIncrease()
    {
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2300, 2800, 3500, 4200, 5100], // steadily increasing, no gap
            BaselineRtMs: [2000, 2100, 1900, 2050, 2000, 2100],
            RecentAccuracies: [1, 1, 0, 0, 1, 0]
        );
        var result = _detector.Detect(input);
        Assert.Equal(MindWanderingState.UnawareDrift, result.State);
        Assert.Null(result.GapIndex);
        Assert.True(result.Confidence >= 0.6);
    }

    [Fact]
    public void Focused_WhenRtVarianceWithinBaseline()
    {
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2100, 1900, 2050, 2000],
            BaselineRtMs: [2000, 2100, 1900, 2050, 2000],
            RecentAccuracies: [1, 1, 1, 1, 1]
        );
        var result = _detector.Detect(input);
        Assert.Equal(MindWanderingState.Focused, result.State);
        Assert.Null(result.GapIndex);
    }

    [Fact]
    public void Focused_WhenInsufficientData()
    {
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2100], // only 2 data points
            BaselineRtMs: [2000],
            RecentAccuracies: [1, 1]
        );
        var result = _detector.Detect(input);
        Assert.Equal(MindWanderingState.Focused, result.State);
        Assert.Equal(0.5, result.Confidence);
    }

    [Fact]
    public void UnawareDrift_NotDetectedWithTooFewQuestions()
    {
        // Only 4 questions — below the 5-question threshold for unaware drift
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2500, 3000, 3500],
            BaselineRtMs: [2000, 2100, 1900, 2050],
            RecentAccuracies: [1, 0, 0, 0]
        );
        var result = _detector.Detect(input);
        // Should not classify as UnawareDrift with only 4 questions
        Assert.NotEqual(MindWanderingState.UnawareDrift, result.State);
    }

    [Fact]
    public void AwareDrift_NotDetectedWhenMultipleGaps()
    {
        // Two gaps — this is a different pattern (not aware drift)
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 8000, 2000, 8000, 2000],
            BaselineRtMs: [2000, 2100, 1900, 2050, 2000],
            RecentAccuracies: [1, 0, 1, 0, 1]
        );
        var result = _detector.Detect(input);
        Assert.NotEqual(MindWanderingState.AwareDrift, result.State);
    }

    [Fact]
    public void AwareDrift_GapAtEnd_NoRecovery_NotAware()
    {
        // Gap at the last question — no recovery data available
        // Should not be classified as aware drift (can't confirm recovery)
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2100, 1900, 2050, 8500],
            BaselineRtMs: [2000, 2100, 1900, 2050, 2000],
            RecentAccuracies: [1, 1, 1, 1, 0]
        );
        var result = _detector.Detect(input);
        // Gap at last position with no subsequent recovery — can't confirm aware
        Assert.NotEqual(MindWanderingState.AwareDrift, result.State);
    }

    [Fact]
    public void UnawareDrift_SteepSlope_HighConfidence()
    {
        var input = new MindWanderingInput(
            RecentRtMs: [2000, 2600, 3200, 3800, 4500, 5200, 6000],
            BaselineRtMs: [2000, 2100, 1900, 2050, 2000, 2100, 1950],
            RecentAccuracies: [1, 1, 0, 0, 0, 0, 0]
        );
        var result = _detector.Detect(input);
        Assert.Equal(MindWanderingState.UnawareDrift, result.State);
        Assert.True(result.Confidence >= 0.7, $"Expected confidence >= 0.7, got {result.Confidence}");
    }

    [Fact]
    public void Confidence_AlwaysBetween0And1()
    {
        var scenarios = new[]
        {
            new MindWanderingInput([2000, 2100, 8500, 2200, 1900], [2000, 2100, 1900, 2050, 2000], [1, 1, 0, 1, 1]),
            new MindWanderingInput([2000, 2300, 2800, 3500, 4200, 5100], [2000, 2100, 1900, 2050, 2000, 2100], [1, 1, 0, 0, 1, 0]),
            new MindWanderingInput([2000, 2100, 1900], [2000, 2100, 1900], [1, 1, 1]),
        };

        foreach (var input in scenarios)
        {
            var result = _detector.Detect(input);
            Assert.InRange(result.Confidence, 0.0, 1.0);
        }
    }
}
