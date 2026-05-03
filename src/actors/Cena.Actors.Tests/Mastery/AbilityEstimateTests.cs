// =============================================================================
// RDY-071 Phase 1A — AbilityEstimate + MasteryTrajectory tests
//
// Proves the honest-labeling guards:
//   * Bucket returns Inconclusive when SampleSize is below threshold.
//   * Bucket returns Inconclusive when 80% CI straddles a boundary
//     (even with plenty of samples — honesty > speed).
//   * Caption includes the sample-size + window so the student sees
//     exactly what evidence backs the bucket claim.
//   * Zero path produces a numeric Bagrut score — that view is gated
//     elsewhere (ConcordanceMapping.F8PointEstimateEnabled).
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public class AbilityEstimateTests
{
    private static AbilityEstimate Est(double theta, double se, int n = 50)
        => new(
            StudentAnonId: "stu-anon-1",
            TopicSlug: "algebra",
            Theta: theta,
            StandardError: se,
            SampleSize: n,
            ComputedAtUtc: DateTimeOffset.UtcNow,
            ObservationWindowWeeks: 6);

    [Fact]
    public void CI_half_width_is_1_282_times_SE()
    {
        var e = Est(theta: 0.0, se: 0.5);
        // 1.2816 × 0.5 = 0.6408
        Assert.InRange(e.ConfidenceInterval80, 0.64, 0.642);
        Assert.InRange(e.LowerBound80, -0.642, -0.64);
        Assert.InRange(e.UpperBound80, 0.64, 0.642);
    }

    [Theory]
    [InlineData(0, 29)]           // sample size just under threshold
    [InlineData(0, 0)]            // no samples at all
    [InlineData(-1.5, 15)]        // low theta but too few samples
    public void Bucket_is_inconclusive_below_minimum_samples(double theta, int n)
    {
        var e = Est(theta, se: 0.2, n);
        Assert.Equal(MasteryBucket.Inconclusive, e.Bucket());
    }

    [Fact]
    public void Bucket_is_low_when_upper_bound_below_low_threshold()
    {
        // theta = -1.0, SE = 0.2 → upper bound = -1.0 + 0.256 = -0.744 < -0.5 ✓
        var e = Est(theta: -1.0, se: 0.2, n: 80);
        Assert.Equal(MasteryBucket.Low, e.Bucket());
    }

    [Fact]
    public void Bucket_is_high_when_lower_bound_above_high_threshold()
    {
        // theta = 1.0, SE = 0.2 → lower bound = 1.0 - 0.256 = 0.744 > 0.5 ✓
        var e = Est(theta: 1.0, se: 0.2, n: 80);
        Assert.Equal(MasteryBucket.High, e.Bucket());
    }

    [Fact]
    public void Bucket_is_medium_when_CI_contained_in_medium_band()
    {
        // theta = 0.0, SE = 0.2 → CI [-0.256, +0.256], both in [-0.5, +0.5] ✓
        var e = Est(theta: 0.0, se: 0.2, n: 80);
        Assert.Equal(MasteryBucket.Medium, e.Bucket());
    }

    [Theory]
    [InlineData(-0.45, 0.2)]  // lower bound -0.706 straddles -0.5
    [InlineData(+0.45, 0.2)]  // upper bound +0.706 straddles +0.5
    [InlineData(0.0, 0.5)]    // CI [-0.64, +0.64] straddles BOTH
    public void Bucket_is_inconclusive_when_CI_straddles_boundary(double theta, double se)
    {
        var e = Est(theta, se, n: 80);
        Assert.Equal(MasteryBucket.Inconclusive, e.Bucket());
    }

    [Fact]
    public void Bucket_threshold_is_configurable()
    {
        // Configure min sample size = 100; a 50-sample estimate is now inconclusive
        var e = Est(theta: 0.0, se: 0.2, n: 50);
        Assert.Equal(MasteryBucket.Inconclusive, e.Bucket(minimumSampleSize: 100));
    }
}

public class MasteryTrajectoryTests
{
    private static AbilityEstimate Est(DateTimeOffset at, double theta, int n = 50, int weeks = 6)
        => new(
            StudentAnonId: "stu-anon-1",
            TopicSlug: "algebra",
            Theta: theta,
            StandardError: 0.2,
            SampleSize: n,
            ComputedAtUtc: at,
            ObservationWindowWeeks: weeks);

    [Fact]
    public void FromEstimates_throws_on_empty()
    {
        Assert.Throws<ArgumentException>(() =>
            MasteryTrajectory.FromEstimates(Array.Empty<AbilityEstimate>()));
    }

    [Fact]
    public void CurrentBucket_matches_last_point()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-42);
        var trajectory = MasteryTrajectory.FromEstimates(new[]
        {
            Est(start,                       theta: -1.0, n: 80),
            Est(start.AddDays(14),           theta:  0.0, n: 100),
            Est(start.AddDays(28),           theta:  1.0, n: 120), // final: HIGH
        });
        Assert.Equal(MasteryBucket.High, trajectory.CurrentBucket);
    }

    [Fact]
    public void Caption_for_inconclusive_includes_keep_practicing_copy()
    {
        // Small sample → inconclusive
        var trajectory = MasteryTrajectory.FromEstimates(new[]
        {
            Est(DateTimeOffset.UtcNow, theta: 0.0, n: 10, weeks: 1),
        });
        Assert.Equal(MasteryBucket.Inconclusive, trajectory.CurrentBucket);
        Assert.Contains("Keep practicing", trajectory.CurrentCaption);
        Assert.Contains("10 problems", trajectory.CurrentCaption);
        Assert.Contains("1 weeks", trajectory.CurrentCaption);
    }

    [Fact]
    public void Caption_for_medium_renders_honest_sample_size_window()
    {
        var trajectory = MasteryTrajectory.FromEstimates(new[]
        {
            Est(DateTimeOffset.UtcNow, theta: 0.0, n: 100, weeks: 6),
        });
        var caption = trajectory.CurrentCaption;
        Assert.Contains("MEDIUM", caption);
        Assert.Contains("80% confidence", caption);
        Assert.Contains("100 problems", caption);
        Assert.Contains("6 weeks", caption);
        // NEVER a numeric Bagrut prediction.
        Assert.DoesNotContain("Bagrut", caption);
        Assert.DoesNotContain("predicted", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Caption_never_contains_predicted_bagrut_phrases()
    {
        // Exhaustive: no matter what the bucket, caption avoids the
        // banned phrases enforced by the shipgate scanner.
        foreach (var theta in new[] { -1.5, -0.3, 0.0, 0.3, 1.5 })
        {
            var t = MasteryTrajectory.FromEstimates(new[]
            {
                Est(DateTimeOffset.UtcNow, theta: theta, n: 100),
            });
            var c = t.CurrentCaption;
            Assert.DoesNotContain("predicted", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bagrut", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expected score", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("will score", c, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Points_preserve_time_order_and_bucket_per_point()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-21);
        var trajectory = MasteryTrajectory.FromEstimates(new[]
        {
            Est(start,                theta: -1.0, n: 50),
            Est(start.AddDays(7),     theta: -0.1, n: 80),
            Est(start.AddDays(14),    theta:  0.9, n: 100),
        });
        Assert.Equal(3, trajectory.Points.Length);
        Assert.Equal(MasteryBucket.Low, trajectory.Points[0].Bucket);
        Assert.Equal(MasteryBucket.Medium, trajectory.Points[1].Bucket);
        Assert.Equal(MasteryBucket.High, trajectory.Points[2].Bucket);
    }
}
