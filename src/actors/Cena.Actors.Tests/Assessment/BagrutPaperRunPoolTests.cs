// =============================================================================
// Cena Platform — BagrutPaperRunPool helper tests (PRR-291)
//
// Pins the pure helpers that drive cohort-fairness bucketing + key
// composition. The full SubmitAsync → cohort-share roundtrip lives in the
// real-Postgres integration suite; these tests pin the deterministic
// transform that's the same problem at a smaller scale.
// =============================================================================

using Cena.Actors.Assessment;

namespace Cena.Actors.Tests.Assessment;

public sealed class BagrutPaperRunPoolTests
{
    [Theory]
    // 8 buckets/day aligned to UTC midnight: [00–03), [03–06), [06–09),
    // [09–12), [12–15), [15–18), [18–21), [21–24). 14:30 falls in [12–15)
    // so it buckets to 12. A 3-hour Ministry sitting that starts at 14:00
    // straddles two buckets (12 and 15); that's a known v1 limitation —
    // dynamic event-based windowing is a future task.
    [InlineData(2026, 5, 1, 14, 30, 12)]   // 14:30 → window starts 12:00 (in [12–15) bucket)
    [InlineData(2026, 5, 1,  0,  0,  0)]   // midnight is a window boundary
    [InlineData(2026, 5, 1,  2, 59,  0)]   // last minute of [00–03) → 00
    [InlineData(2026, 5, 1,  3,  0,  3)]   // first minute of [03–06) → 03
    [InlineData(2026, 5, 1, 14, 59, 12)]   // last minute of [12–15) → 12
    [InlineData(2026, 5, 1, 15,  0, 15)]   // first minute of [15–18) → 15
    [InlineData(2026, 5, 1, 23, 59, 21)]   // last bucket of the day → 21
    public void ComputeWindowStart_BucketsToCanonical3HourGrid(
        int year, int month, int day, int hour, int minute, int expectedHour)
    {
        var now = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
        var actual = BagrutPaperRunPool.ComputeWindowStart(now);
        Assert.Equal(expectedHour, actual.Hour);
        Assert.Equal(0, actual.Minute);
        Assert.Equal(0, actual.Second);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Fact]
    public void ComputeWindowStart_NormalisesNonUtcOffset_To_Utc()
    {
        // A start time given in IDT (+03) at 16:30 local → 13:30 UTC →
        // window 12:00 UTC (in [12–15) bucket). The bucketer MUST use UTC
        // hours so two students in different timezones during the same
        // wall-clock window converge on the same bucket.
        var localIDT = new DateTimeOffset(2026, 5, 1, 16, 30, 0, TimeSpan.FromHours(3));
        var actual = BagrutPaperRunPool.ComputeWindowStart(localIDT);
        Assert.Equal(12, actual.Hour);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Fact]
    public void ComposeId_StableShape_ForDashboardAndIncidentResponse()
    {
        var window = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal("035582|2026-05-01T14",
            BagrutPaperRunPool.ComposeId("035582", window));
    }

    [Fact]
    public void ComputeCohortSeed_IsStableAcrossInvocations_AndProcesses()
    {
        // Stable hash (FNV-1a) — independent of .NET 5+ randomized
        // string.GetHashCode(). Same inputs MUST yield same seed across
        // process restarts so the cohort seeding is durable.
        var window = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero);
        var seedA = BagrutPaperRunPool.ComputeCohortSeed("035582", window);
        var seedB = BagrutPaperRunPool.ComputeCohortSeed("035582", window);
        Assert.Equal(seedA, seedB);
    }

    [Fact]
    public void ComputeCohortSeed_DifferentPaperCodes_DifferentSeeds()
    {
        var window = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero);
        var math = BagrutPaperRunPool.ComputeCohortSeed("035582", window);
        var phys = BagrutPaperRunPool.ComputeCohortSeed("036991", window);
        Assert.NotEqual(math, phys);
    }

    [Fact]
    public void ComputeCohortSeed_DifferentWindows_DifferentSeeds()
    {
        var w14 = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero);
        var w17 = new DateTimeOffset(2026, 5, 1, 17, 0, 0, TimeSpan.Zero);
        var s14 = BagrutPaperRunPool.ComputeCohortSeed("035582", w14);
        var s17 = BagrutPaperRunPool.ComputeCohortSeed("035582", w17);
        Assert.NotEqual(s14, s17);
    }

    [Fact]
    public void ComputeCohortSeed_DeterministicShuffleAcrossSessions()
    {
        // Two RNGs seeded from ComputeCohortSeed must produce identical
        // sequences — that's the load-bearing invariant: cohort member 1
        // who seeded the pool and cohort member 2 (who would re-seed
        // independently if we hadn't persisted) must agree on the shuffle.
        var window = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero);
        var rngA = new Random(BagrutPaperRunPool.ComputeCohortSeed("035582", window));
        var rngB = new Random(BagrutPaperRunPool.ComputeCohortSeed("035582", window));
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(rngA.Next(), rngB.Next());
        }
    }

    [Fact]
    public void WindowHours_Is3()
    {
        // Pinning: real Bagrut sittings are ~3.5h; 3h windows cover one
        // sitting cleanly. Changing this is a fairness-policy decision
        // that needs an ADR.
        Assert.Equal(3, BagrutPaperRunPool.WindowHours);
    }
}
