// =============================================================================
// Cena Platform — SessionRiskAssessment unit tests
// prr-013 / ADR-0003 / RDY-080
// =============================================================================

using Cena.Actors.Sessions;

namespace Cena.Actors.Tests.Sessions;

public sealed class SessionRiskAssessmentTests
{
    [Fact]
    public void ValidConstruction_ComputesClippedBounds()
    {
        var now = DateTimeOffset.UtcNow;
        var a = new SessionRiskAssessment(
            pointEstimate: 0.40,
            confidenceIntervalHalfWidth: 0.12,
            sampleSize: 18,
            generatedAt: now);

        Assert.Equal(0.40, a.PointEstimate, 6);
        Assert.Equal(0.12, a.ConfidenceIntervalHalfWidth, 6);
        Assert.Equal(18, a.SampleSize);
        Assert.Equal(now, a.GeneratedAt);
        Assert.Equal(0.28, a.LowerBound, 6);
        Assert.Equal(0.52, a.UpperBound, 6);

        // Clipping at both ends
        var extreme = new SessionRiskAssessment(0.05, 0.20, 4, now);
        Assert.Equal(0.0, extreme.LowerBound, 6);
        var top = new SessionRiskAssessment(0.95, 0.20, 4, now);
        Assert.Equal(1.0, top.UpperBound, 6);
    }

    [Fact]
    public void PointEstimateOutsideUnitInterval_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionRiskAssessment(-0.01, 0.10, 5, now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionRiskAssessment(1.01, 0.10, 5, now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionRiskAssessment(double.NaN, 0.10, 5, now));
    }

    [Fact]
    public void NonPositiveSampleSize_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionRiskAssessment(0.4, 0.1, 0, now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionRiskAssessment(0.4, 0.1, -3, now));
    }
}
