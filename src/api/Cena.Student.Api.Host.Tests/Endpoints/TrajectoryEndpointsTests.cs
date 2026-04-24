// =============================================================================
// RDY-071 Phase 1B — TrajectoryEndpoints response-shape tests.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Api.Host.Endpoints;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class TrajectoryEndpointsResponseTests
{
    [Fact]
    public void Null_trajectory_becomes_empty_keep_practicing()
    {
        var dto = TrajectoryEndpoints.ToResponse(
            studentAnonId: "stu-1",
            topicSlug: "derivatives",
            trajectory: null);

        Assert.Equal(MasteryBucket.Inconclusive.ToString(), dto.CurrentBucket);
        Assert.Empty(dto.Points);
        Assert.Equal(0, dto.TotalSampleSize);
        Assert.Contains("Keep practicing", dto.CurrentCaption);
        Assert.DoesNotContain("Bagrut", dto.CurrentCaption);
        Assert.DoesNotContain("predicted", dto.CurrentCaption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Populated_trajectory_renders_bucket_and_caption()
    {
        var now = DateTimeOffset.UtcNow;
        var estimate = new AbilityEstimate(
            StudentAnonId: "stu-1",
            TopicSlug: "derivatives",
            Theta: 1.0,
            StandardError: 0.2,
            SampleSize: 100,
            ComputedAtUtc: now,
            ObservationWindowWeeks: 6);
        var trajectory = MasteryTrajectory.FromEstimates(new[] { estimate });

        var dto = TrajectoryEndpoints.ToResponse(
            studentAnonId: "stu-1",
            topicSlug: "derivatives",
            trajectory: trajectory);

        Assert.Equal("High", dto.CurrentBucket);
        Assert.Single(dto.Points);
        Assert.Equal(100, dto.TotalSampleSize);
        Assert.Equal(6, dto.WindowWeeks);
        Assert.Contains("HIGH", dto.CurrentCaption);
        Assert.Contains("80% confidence", dto.CurrentCaption);
    }

    [Fact]
    public void Response_never_contains_forward_extrapolation_phrases()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new AbilityEstimate("stu-1", "derivatives", -1.5, 0.2, 60, now.AddDays(-28), 4),
            new AbilityEstimate("stu-1", "derivatives",  0.0, 0.2, 80, now.AddDays(-14), 4),
            new AbilityEstimate("stu-1", "derivatives",  1.0, 0.2, 100, now,             6),
        };
        var trajectory = MasteryTrajectory.FromEstimates(samples);
        var dto = TrajectoryEndpoints.ToResponse("stu-1", "derivatives", trajectory);

        var haystack = dto.CurrentCaption
            + string.Join("|", dto.Points.Select(p => p.Bucket));

        Assert.DoesNotContain("predicted bagrut", haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("your bagrut score", haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will score", haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expected grade", haystack, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_point_array_preserved_when_trajectory_is_empty_collection()
    {
        // Defensive: a caller that hands us a MasteryTrajectory with
        // an empty ImmutableArray<Point> should still get the empty
        // "keep practicing" response rather than a NullReferenceException.
        var empty = new MasteryTrajectory(
            StudentAnonId: "stu-1",
            TopicSlug: "derivatives",
            Points: ImmutableArray<TrajectoryPoint>.Empty,
            TotalSampleSize: 0,
            WindowWeeks: 0);

        var dto = TrajectoryEndpoints.ToResponse("stu-1", "derivatives", empty);
        Assert.Empty(dto.Points);
        Assert.Contains("Keep practicing", dto.CurrentCaption);
    }
}

public class NullMasteryTrajectoryProviderTests
{
    [Fact]
    public async Task Always_returns_null()
    {
        var provider = new NullMasteryTrajectoryProvider();
        var result = await provider.GetAsync("stu-1", "derivatives", default);
        Assert.Null(result);
    }
}
