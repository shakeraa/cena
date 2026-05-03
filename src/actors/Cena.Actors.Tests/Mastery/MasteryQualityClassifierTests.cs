// =============================================================================
// MST-012 Tests: Mastery quality matrix classifier
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class MasteryQualityClassifierTests
{
    [Fact]
    public void Classify_FastCorrect_ReturnsMastered()
    {
        var quality = MasteryQualityClassifier.Classify(
            isCorrect: true, responseTimeMs: 5_000, medianResponseTimeMs: 12_000f);

        Assert.Equal(MasteryQuality.Mastered, quality);
    }

    [Fact]
    public void Classify_SlowCorrect_ReturnsEffortful()
    {
        var quality = MasteryQualityClassifier.Classify(
            isCorrect: true, responseTimeMs: 18_000, medianResponseTimeMs: 12_000f);

        Assert.Equal(MasteryQuality.Effortful, quality);
    }

    [Fact]
    public void Classify_FastIncorrect_ReturnsCareless()
    {
        var quality = MasteryQualityClassifier.Classify(
            isCorrect: false, responseTimeMs: 3_000, medianResponseTimeMs: 12_000f);

        Assert.Equal(MasteryQuality.Careless, quality);
    }

    [Fact]
    public void Classify_SlowIncorrect_ReturnsStruggling()
    {
        var quality = MasteryQualityClassifier.Classify(
            isCorrect: false, responseTimeMs: 25_000, medianResponseTimeMs: 12_000f);

        Assert.Equal(MasteryQuality.Struggling, quality);
    }

    [Fact]
    public void Classify_ExactlyAtMedian_CountsAsSlow()
    {
        // Equal to median = not fast
        var quality = MasteryQualityClassifier.Classify(
            isCorrect: true, responseTimeMs: 12_000, medianResponseTimeMs: 12_000f);

        Assert.Equal(MasteryQuality.Effortful, quality);
    }

    [Fact]
    public void ResponseTimeBaseline_UpdatesMedian()
    {
        var baseline = ResponseTimeBaseline.Initial;

        baseline = baseline.Update(10_000);
        baseline = baseline.Update(12_000);
        baseline = baseline.Update(14_000);
        baseline = baseline.Update(8_000);
        baseline = baseline.Update(20_000);

        // Sorted: 8000, 10000, 12000, 14000, 20000 -> median = 12000
        Assert.Equal(12_000f, baseline.MedianResponseTimeMs);
        Assert.Equal(5, baseline.SampleCount);
    }

    [Fact]
    public void ResponseTimeBaseline_FewSamples_UsesDefault()
    {
        var baseline = ResponseTimeBaseline.Initial;

        baseline = baseline.Update(5_000); // only 1 sample

        // With < 3 samples, should still use default 15000ms
        Assert.Equal(15_000f, baseline.MedianResponseTimeMs);
    }

    [Fact]
    public void ResponseTimeBaseline_ThreeSamples_ComputesMedian()
    {
        var baseline = ResponseTimeBaseline.Initial;

        baseline = baseline.Update(10_000);
        baseline = baseline.Update(20_000);
        baseline = baseline.Update(15_000);

        // Sorted: 10000, 15000, 20000 -> median = 15000
        Assert.Equal(15_000f, baseline.MedianResponseTimeMs);
    }

    [Fact]
    public void ResponseTimeBaseline_CircularBuffer_EvictsOldest()
    {
        var baseline = ResponseTimeBaseline.Initial;

        // Fill with 20 samples of 10_000
        for (int i = 0; i < 20; i++)
            baseline = baseline.Update(10_000);

        Assert.Equal(10_000f, baseline.MedianResponseTimeMs);

        // Add 10 more at 20_000 — should evict old values
        for (int i = 0; i < 10; i++)
            baseline = baseline.Update(20_000);

        // Now buffer has 10x10000 + 10x20000
        // Sorted: [10000x10, 20000x10] -> median = (10000+20000)/2 = 15000
        Assert.Equal(15_000f, baseline.MedianResponseTimeMs);
        Assert.Equal(20, baseline.ResponseTimes.Length); // buffer capped at 20
    }

    [Fact]
    public void ClassifyAndUpdate_ReturnsQualityAndUpdatedBaseline()
    {
        var baseline = ResponseTimeBaseline.Initial;
        baseline = baseline.Update(10_000);
        baseline = baseline.Update(12_000);
        baseline = baseline.Update(14_000); // median now = 12000

        var (quality, updated) = MasteryQualityClassifier.ClassifyAndUpdate(
            isCorrect: true, responseTimeMs: 8_000, baseline);

        Assert.Equal(MasteryQuality.Mastered, quality); // fast + correct
        Assert.Equal(4, updated.SampleCount);
    }
}
