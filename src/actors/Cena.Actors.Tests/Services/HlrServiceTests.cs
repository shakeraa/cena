using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

public sealed class HlrServiceTests
{
    private readonly HlrService _sut = new();

    // ── ComputeRecall: p(t) = 2^(-delta/h) ──

    [Fact]
    public void ComputeRecall_AtZeroTime_ReturnsOne()
    {
        Assert.Equal(1.0, _sut.ComputeRecall(24.0, 0.0));
    }

    [Fact]
    public void ComputeRecall_AtOneHalfLife_ReturnsHalf()
    {
        double recall = _sut.ComputeRecall(24.0, 24.0);
        Assert.Equal(0.5, recall, precision: 10);
    }

    [Fact]
    public void ComputeRecall_AtTwoHalfLives_ReturnsQuarter()
    {
        double recall = _sut.ComputeRecall(24.0, 48.0);
        Assert.Equal(0.25, recall, precision: 10);
    }

    [Fact]
    public void ComputeRecall_ZeroHalfLife_ReturnsZero()
    {
        Assert.Equal(0.0, _sut.ComputeRecall(0.0, 10.0));
    }

    [Fact]
    public void ComputeRecall_NegativeHours_ReturnsOne()
    {
        Assert.Equal(1.0, _sut.ComputeRecall(24.0, -5.0));
    }

    // ── ComputeTimeToThreshold ──

    [Fact]
    public void ComputeTimeToThreshold_DefaultThreshold_ReturnsCorrectTime()
    {
        // threshold=0.85, h=24 → t = -24 * log2(0.85)
        var time = _sut.ComputeTimeToThreshold(24.0, 0.85);
        double expectedHours = -24.0 * Math.Log2(0.85);
        Assert.Equal(expectedHours, time.TotalHours, precision: 6);
    }

    [Fact]
    public void ComputeTimeToThreshold_ZeroHalfLife_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, _sut.ComputeTimeToThreshold(0.0));
    }

    // ── UpdateHalfLife ──

    [Fact]
    public void UpdateHalfLife_CorrectAnswer_IncreasesHalfLife()
    {
        double updated = _sut.UpdateHalfLife(24.0, wasCorrect: true, responseTimeMs: 5000);
        Assert.True(updated > 24.0, $"Correct answer should increase half-life, got {updated}");
    }

    [Fact]
    public void UpdateHalfLife_IncorrectAnswer_DecreasesHalfLife()
    {
        double updated = _sut.UpdateHalfLife(24.0, wasCorrect: false, responseTimeMs: 5000);
        Assert.True(updated < 24.0, $"Incorrect answer should decrease half-life, got {updated}");
    }

    [Fact]
    public void UpdateHalfLife_FastCorrect_GrowsMoreThanSlowCorrect()
    {
        double fast = _sut.UpdateHalfLife(24.0, true, responseTimeMs: 1000);
        double slow = _sut.UpdateHalfLife(24.0, true, responseTimeMs: 10000);

        Assert.True(fast > slow,
            $"Fast correct ({fast}) should grow more than slow correct ({slow})");
    }

    [Fact]
    public void UpdateHalfLife_NeverBelowMinimum()
    {
        // Many incorrect answers should not drop below 1 hour
        double h = 2.0;
        for (int i = 0; i < 20; i++)
            h = _sut.UpdateHalfLife(h, false, 5000);

        Assert.True(h >= 1.0, $"Half-life should never drop below 1h, got {h}");
    }

    [Fact]
    public void UpdateHalfLife_NeverAboveMaximum()
    {
        // Many correct answers should not exceed 4320 hours (~6 months)
        double h = 100.0;
        for (int i = 0; i < 50; i++)
            h = _sut.UpdateHalfLife(h, true, 1000);

        Assert.True(h <= 4320.0, $"Half-life should never exceed 4320h, got {h}");
    }
}
