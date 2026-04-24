// =============================================================================
// RES-010: Feature flag service tests
// =============================================================================

using Cena.Actors.Infrastructure;
using Cena.Actors.Projections;

namespace Cena.Actors.Tests.Infrastructure;

public sealed class FeatureFlagTests
{
    [Fact]
    public void GetRolloutBucket_Deterministic()
    {
        var bucket1 = FeatureFlagActor.GetRolloutBucket("student-123", "llm.kimi.enabled");
        var bucket2 = FeatureFlagActor.GetRolloutBucket("student-123", "llm.kimi.enabled");
        Assert.Equal(bucket1, bucket2);
    }

    [Fact]
    public void GetRolloutBucket_DifferentStudents_DifferentBuckets()
    {
        var bucket1 = FeatureFlagActor.GetRolloutBucket("student-aaa", "llm.kimi.enabled");
        var bucket2 = FeatureFlagActor.GetRolloutBucket("student-zzz", "llm.kimi.enabled");
        // Not strictly guaranteed but overwhelmingly likely with SHA256
        Assert.NotEqual(bucket1, bucket2);
    }

    [Fact]
    public void GetRolloutBucket_InRange0To100()
    {
        for (int i = 0; i < 100; i++)
        {
            var bucket = FeatureFlagActor.GetRolloutBucket($"student-{i}", "test.flag");
            Assert.InRange(bucket, 0.0, 99.99);
        }
    }

    [Fact]
    public void GetRolloutBucket_100Pct_IncludesAll()
    {
        // Every student should be < 100.0
        for (int i = 0; i < 50; i++)
        {
            var bucket = FeatureFlagActor.GetRolloutBucket($"s-{i}", "flag");
            Assert.True(bucket < 100.0);
        }
    }

    [Fact]
    public void GetRolloutBucket_50Pct_ApproximatelyHalf()
    {
        int inRollout = 0;
        int total = 1000;
        for (int i = 0; i < total; i++)
        {
            var bucket = FeatureFlagActor.GetRolloutBucket($"student-{i}", "test.flag");
            if (bucket < 50.0) inRollout++;
        }
        // Allow 10% tolerance (400-600 out of 1000)
        Assert.InRange(inRollout, 400, 600);
    }

    [Fact]
    public void FeatureFlag_Record_Properties()
    {
        var flag = new FeatureFlagDocument
        {
            Name = "test",
            Enabled = true,
            RolloutPercent = 75.0,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Assert.Equal("test", flag.Name);
        Assert.True(flag.Enabled);
        Assert.Equal(75.0, flag.RolloutPercent);
    }
}
