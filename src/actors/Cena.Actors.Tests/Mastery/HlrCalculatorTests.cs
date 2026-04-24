// =============================================================================
// MST-003 Tests: Half-Life Regression decay engine
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class HlrCalculatorTests
{
    [Fact]
    public void ComputeRecall_AtHalfLife_Returns50Percent()
    {
        var h = 168f;
        var elapsed = TimeSpan.FromHours(168);

        var recall = HlrCalculator.ComputeRecall(h, elapsed);

        Assert.InRange(recall, 0.49f, 0.51f);
    }

    [Fact]
    public void ComputeRecall_JustPracticed_Returns100Percent()
    {
        var recall = HlrCalculator.ComputeRecall(168f, TimeSpan.Zero);
        Assert.Equal(1.0f, recall);
    }

    [Fact]
    public void ComputeRecall_VeryOld_ReturnsNearZero()
    {
        var recall = HlrCalculator.ComputeRecall(24f, TimeSpan.FromDays(365));
        Assert.True(recall < 0.001f);
    }

    [Fact]
    public void ComputeRecall_ZeroHalfLife_ReturnsZero()
    {
        Assert.Equal(0f, HlrCalculator.ComputeRecall(0f, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ComputeRecall_NegativeElapsed_ReturnsOne()
    {
        Assert.Equal(1.0f, HlrCalculator.ComputeRecall(168f, TimeSpan.FromHours(-1)));
    }

    [Fact]
    public void ComputeHalfLife_MorePractice_LongerHalfLife()
    {
        var weights = HlrWeights.Default;

        var fresh = HlrCalculator.ComputeHalfLife(
            new HlrFeatures(AttemptCount: 1, CorrectCount: 1, ConceptDifficulty: 0.5f,
                PrerequisiteDepth: 2, BloomLevel: 2, DaysSinceFirstEncounter: 1), weights);

        var practiced = HlrCalculator.ComputeHalfLife(
            new HlrFeatures(AttemptCount: 20, CorrectCount: 18, ConceptDifficulty: 0.5f,
                PrerequisiteDepth: 2, BloomLevel: 4, DaysSinceFirstEncounter: 30), weights);

        Assert.True(practiced > fresh,
            $"Practiced h={practiced}h should > fresh h={fresh}h");
    }

    [Fact]
    public void ComputeHalfLife_Clamped_MinimumOneHour()
    {
        var weights = new HlrWeights(new float[] { -10, -10, -10, -10, -10, -10 }, Bias: -20);
        var features = new HlrFeatures(0, 0, 1.0f, 10, 0, 0);

        var h = HlrCalculator.ComputeHalfLife(features, weights);
        Assert.True(h >= 1.0f, "Half-life must be at least 1 hour");
    }

    [Fact]
    public void ComputeHalfLife_Clamped_MaximumOneYear()
    {
        var weights = new HlrWeights(new float[] { 10, 10, 10, 10, 10, 10 }, Bias: 50);
        var features = new HlrFeatures(100, 100, 0, 0, 6, 365);

        var h = HlrCalculator.ComputeHalfLife(features, weights);
        Assert.True(h <= 8760f, "Half-life must be at most 1 year");
    }

    [Fact]
    public void ScheduleNextReview_DefaultThreshold_CorrectTiming()
    {
        var h = 168f;
        var nextReview = HlrCalculator.ScheduleNextReview(h, threshold: 0.85f);

        // 0.85 = 2^(-t/168) -> t = -168 * log2(0.85) ~ 168 * 0.2345 ~ 39.4 hours
        Assert.InRange(nextReview.TotalHours, 38, 41);
    }

    [Fact]
    public void ScheduleNextReview_ZeroHalfLife_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, HlrCalculator.ScheduleNextReview(0f));
    }

    [Fact]
    public void UpdateState_UpdatesHalfLifeOnly()
    {
        var state = new ConceptMasteryState { MasteryProbability = 0.80f, HalfLifeHours = 24f };
        var features = new HlrFeatures(10, 8, 0.5f, 2, 3, 14);

        var updated = HlrCalculator.UpdateState(state, features, HlrWeights.Default);

        Assert.NotEqual(24f, updated.HalfLifeHours);
        Assert.Equal(0.80f, updated.MasteryProbability); // unchanged
    }
}
