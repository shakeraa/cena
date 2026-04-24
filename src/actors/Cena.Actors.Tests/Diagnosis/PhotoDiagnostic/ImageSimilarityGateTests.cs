// =============================================================================
// Cena Platform — ImageSimilarityGate tests (EPIC-PRR-J PRR-404)
//
// Pins the gate's policy shape:
//   - First upload always Accept.
//   - Exact-same hash within window → Reject (distance 0).
//   - Near-duplicate within window (dist ≤ threshold) → Reject.
//   - Same hash OUTSIDE window → Accept.
//   - Different hash within window → Accept.
//   - Configurable threshold honored.
//   - Per-student isolation (student A's uploads don't affect student B).
//
// Uses InMemoryRecentPhotoHashStore as the test fixture — it's the real
// production InMemory implementation, not a hand-rolled mock.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class ImageSimilarityGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    private static ImageSimilarityGate NewGate(ImageSimilarityOptions? options = null)
        => new(new InMemoryRecentPhotoHashStore(), options);

    // Helper: flip N bits of a ulong so we can dial Hamming distance exactly.
    private static ulong FlipBits(ulong hash, int count)
    {
        for (int i = 0; i < count; i++) hash ^= (1UL << i);
        return hash;
    }

    [Fact]
    public async Task FirstUploadIsAlwaysAccepted()
    {
        var gate = NewGate();
        var decision = await gate.EvaluateAsync("student-a", 0xDEADBEEFDEADBEEF, T0, default);
        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);
        Assert.Null(decision.RecentMatch);
    }

    [Fact]
    public async Task ExactDuplicateWithinWindowIsRejectedWithZeroDistance()
    {
        var gate = NewGate();
        const ulong hash = 0x1234567890ABCDEFUL;

        var first = await gate.EvaluateAsync("student-a", hash, T0, default);
        Assert.Equal(ImageSimilarityOutcome.Accept, first.Outcome);

        var second = await gate.EvaluateAsync("student-a", hash, T0.AddMinutes(2), default);
        Assert.Equal(ImageSimilarityOutcome.RejectNearDuplicate, second.Outcome);
        Assert.NotNull(second.RecentMatch);
        Assert.Equal(0, second.RecentMatch!.HammingDistance);
        Assert.Equal(T0, second.RecentMatch.PriorUploadedAt);
    }

    [Fact]
    public async Task NearDuplicateWithinThresholdIsRejected()
    {
        var gate = NewGate(); // default threshold = 10
        const ulong baseline = 0xAAAAAAAAAAAAAAAAUL;
        var near = FlipBits(baseline, 7); // distance 7 ≤ 10

        await gate.EvaluateAsync("student-a", baseline, T0, default);
        var decision = await gate.EvaluateAsync("student-a", near, T0.AddMinutes(1), default);

        Assert.Equal(ImageSimilarityOutcome.RejectNearDuplicate, decision.Outcome);
        Assert.Equal(7, decision.RecentMatch!.HammingDistance);
    }

    [Fact]
    public async Task DistanceJustAboveThresholdIsAccepted()
    {
        var gate = NewGate(); // default threshold = 10
        const ulong baseline = 0xAAAAAAAAAAAAAAAAUL;
        var far = FlipBits(baseline, 11); // distance 11 > 10

        await gate.EvaluateAsync("student-a", baseline, T0, default);
        var decision = await gate.EvaluateAsync("student-a", far, T0.AddMinutes(1), default);

        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);
        Assert.Null(decision.RecentMatch);
    }

    [Fact]
    public async Task SameHashOutsideWindowIsAccepted()
    {
        var gate = NewGate(); // window = 5 min
        const ulong hash = 0x1111111111111111UL;

        await gate.EvaluateAsync("student-a", hash, T0, default);
        // 6 minutes later: outside the 5-minute window.
        var decision = await gate.EvaluateAsync("student-a", hash, T0.AddMinutes(6), default);

        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);
    }

    [Fact]
    public async Task CompletelyDifferentHashWithinWindowIsAccepted()
    {
        var gate = NewGate();
        const ulong first = 0x0000000000000000UL;
        const ulong second = 0xFFFFFFFFFFFFFFFFUL; // distance 64

        await gate.EvaluateAsync("student-a", first, T0, default);
        var decision = await gate.EvaluateAsync("student-a", second, T0.AddMinutes(1), default);

        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);
    }

    [Fact]
    public async Task ConfigurableThresholdIsHonored()
    {
        // Tighter threshold: dist-7 near-duplicate now accepted.
        var strict = new ImageSimilarityGate(
            new InMemoryRecentPhotoHashStore(),
            new ImageSimilarityOptions(TimeSpan.FromMinutes(5), MaxHammingDistance: 5));
        const ulong baseline = 0xAAAAAAAAAAAAAAAAUL;
        var seven = FlipBits(baseline, 7);

        await strict.EvaluateAsync("student-a", baseline, T0, default);
        var decision = await strict.EvaluateAsync("student-a", seven, T0.AddMinutes(1), default);
        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);

        // Looser threshold: dist-20 now rejected.
        var loose = new ImageSimilarityGate(
            new InMemoryRecentPhotoHashStore(),
            new ImageSimilarityOptions(TimeSpan.FromMinutes(5), MaxHammingDistance: 25));
        var twenty = FlipBits(baseline, 20);
        await loose.EvaluateAsync("student-a", baseline, T0, default);
        var looseDecision = await loose.EvaluateAsync("student-a", twenty, T0.AddMinutes(1), default);
        Assert.Equal(ImageSimilarityOutcome.RejectNearDuplicate, looseDecision.Outcome);
        Assert.Equal(20, looseDecision.RecentMatch!.HammingDistance);
    }

    [Fact]
    public async Task ConfigurableWindowIsHonored()
    {
        // 1-minute window: 90 seconds later is outside.
        var gate = new ImageSimilarityGate(
            new InMemoryRecentPhotoHashStore(),
            new ImageSimilarityOptions(TimeSpan.FromMinutes(1), MaxHammingDistance: 10));
        const ulong hash = 0xCAFEBABECAFEBABEUL;

        await gate.EvaluateAsync("student-a", hash, T0, default);
        var decision = await gate.EvaluateAsync("student-a", hash, T0.AddSeconds(90), default);
        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);
    }

    [Fact]
    public async Task DifferentStudentsAreIsolated()
    {
        var gate = NewGate();
        const ulong hash = 0x1234123412341234UL;

        await gate.EvaluateAsync("student-a", hash, T0, default);
        // Same hash, same instant, but different student — must accept.
        var decision = await gate.EvaluateAsync("student-b", hash, T0, default);
        Assert.Equal(ImageSimilarityOutcome.Accept, decision.Outcome);
    }

    [Fact]
    public async Task RejectedUploadDoesNotPoisonTheLedger()
    {
        // If a student's second upload is rejected, the first upload (NOT
        // the rejected second) remains the anchor. A third attempt should
        // still compare against the first upload's timestamp.
        var gate = NewGate();
        const ulong hash = 0xBEEFBEEFBEEFBEEFUL;

        var first = await gate.EvaluateAsync("student-a", hash, T0, default);
        Assert.Equal(ImageSimilarityOutcome.Accept, first.Outcome);

        var second = await gate.EvaluateAsync("student-a", hash, T0.AddMinutes(1), default);
        Assert.Equal(ImageSimilarityOutcome.RejectNearDuplicate, second.Outcome);
        Assert.Equal(T0, second.RecentMatch!.PriorUploadedAt);

        var third = await gate.EvaluateAsync("student-a", hash, T0.AddMinutes(4), default);
        Assert.Equal(ImageSimilarityOutcome.RejectNearDuplicate, third.Outcome);
        // If the rejected second had been recorded, this would point at
        // T0+1min. It points at T0 instead, proving rejection didn't
        // update the ledger.
        Assert.Equal(T0, third.RecentMatch!.PriorUploadedAt);
    }

    [Fact]
    public async Task PicksClosestAmongMultipleRecentMatches()
    {
        var gate = NewGate();
        const ulong a = 0x0UL;
        var b = FlipBits(a, 8); // distance 8
        var probe = FlipBits(a, 3); // distance 3 from a, 11 from b

        await gate.EvaluateAsync("student-a", a, T0, default);
        await gate.EvaluateAsync("student-a", b, T0.AddMinutes(1), default);

        var decision = await gate.EvaluateAsync("student-a", probe, T0.AddMinutes(2), default);
        Assert.Equal(ImageSimilarityOutcome.RejectNearDuplicate, decision.Outcome);
        // Closest = a at distance 3, not b at distance 11.
        Assert.Equal(3, decision.RecentMatch!.HammingDistance);
        Assert.Equal(T0, decision.RecentMatch.PriorUploadedAt);
    }

    [Fact]
    public async Task RejectsEmptyStudent()
    {
        var gate = NewGate();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            gate.EvaluateAsync("", 0UL, T0, default));
    }

    [Fact]
    public void RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ImageSimilarityGate(
                new InMemoryRecentPhotoHashStore(),
                new ImageSimilarityOptions(TimeSpan.FromMinutes(5), MaxHammingDistance: -1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ImageSimilarityGate(
                new InMemoryRecentPhotoHashStore(),
                new ImageSimilarityOptions(TimeSpan.FromMinutes(5), MaxHammingDistance: 65)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ImageSimilarityGate(
                new InMemoryRecentPhotoHashStore(),
                new ImageSimilarityOptions(TimeSpan.Zero, MaxHammingDistance: 10)));
    }

    [Fact]
    public async Task InMemoryStoreSweepsEntriesBeyondRetention()
    {
        // Separate test for the sweep: record at T0, then query at
        // T0 + 2h with 1h retention — entry must be gone.
        var store = new InMemoryRecentPhotoHashStore(retention: TimeSpan.FromHours(1));
        await store.RecordAsync("student-a", 0xABCDUL, T0, default);

        var recent = await store.RecentHashesAsync(
            "student-a", T0.AddHours(2).AddMinutes(-5), default);

        Assert.Empty(recent);
    }

    [Fact]
    public async Task InMemoryStoreReturnsOnlyWithinWindowEvenWhenRetentionIsLonger()
    {
        var store = new InMemoryRecentPhotoHashStore(retention: TimeSpan.FromHours(10));
        await store.RecordAsync("student-a", 0x1UL, T0, default);
        await store.RecordAsync("student-a", 0x2UL, T0.AddMinutes(10), default);

        var recent = await store.RecentHashesAsync(
            "student-a", T0.AddMinutes(5), default);

        // Only the T0+10min entry survives the since-filter, despite
        // retention being much longer.
        Assert.Single(recent);
        Assert.Equal(0x2UL, recent[0].PHash);
    }
}
