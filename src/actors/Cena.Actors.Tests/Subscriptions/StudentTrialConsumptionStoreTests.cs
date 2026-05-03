// =============================================================================
// Cena Platform — StudentTrialConsumptionStoreTests (Phase 1D)
//
// Locks the contract of IStudentTrialConsumptionStore (InMemory variant):
//   - GetAsync returns Empty for unseen ids (never null)
//   - IncrementAsync increments only the requested feature
//   - DaysActive counts distinct UTC dates
//   - ResetAsync zeros all counters (idempotent on unseen ids)
//   - Generic feature does not increment any counter
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class StudentTrialConsumptionStoreTests
{
    private const string StudentId = "enc::student::consumption-tests";

    private readonly InMemoryStudentTrialConsumptionStore _store = new();

    private static readonly DateTimeOffset Day1 =
        new(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day1Later =
        new(2026, 4, 28, 17, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 =
        new(2026, 4, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_empty_for_unseen_student()
    {
        var snapshot = await _store.GetAsync("enc::unseen", CancellationToken.None);
        Assert.Equal(StudentTrialConsumption.Empty, snapshot);
    }

    [Fact]
    public async Task GetAsync_returns_empty_for_blank_id()
    {
        var snapshot = await _store.GetAsync("   ", CancellationToken.None);
        Assert.Equal(StudentTrialConsumption.Empty, snapshot);
    }

    [Fact]
    public async Task IncrementAsync_TutorTurn_only_increments_TutorTurnsUsed()
    {
        var post = await _store.IncrementAsync(
            StudentId, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        Assert.Equal(1, post.TutorTurnsUsed);
        Assert.Equal(0, post.PhotoDiagnosticsUsed);
        Assert.Equal(0, post.SessionsStarted);
        Assert.Equal(1, post.DaysActive);
    }

    [Fact]
    public async Task IncrementAsync_PhotoDiagnostic_only_increments_PhotoDiagnosticsUsed()
    {
        var post = await _store.IncrementAsync(
            StudentId, EntitlementFeature.PhotoDiagnostic, Day1, CancellationToken.None);
        Assert.Equal(0, post.TutorTurnsUsed);
        Assert.Equal(1, post.PhotoDiagnosticsUsed);
        Assert.Equal(0, post.SessionsStarted);
    }

    [Fact]
    public async Task IncrementAsync_PracticeSession_only_increments_SessionsStarted()
    {
        var post = await _store.IncrementAsync(
            StudentId, EntitlementFeature.PracticeSession, Day1, CancellationToken.None);
        Assert.Equal(0, post.TutorTurnsUsed);
        Assert.Equal(0, post.PhotoDiagnosticsUsed);
        Assert.Equal(1, post.SessionsStarted);
    }

    [Fact]
    public async Task Generic_feature_does_not_increment_any_counter_but_records_active_day()
    {
        var post = await _store.IncrementAsync(
            StudentId, EntitlementFeature.Generic, Day1, CancellationToken.None);
        Assert.Equal(0, post.TutorTurnsUsed);
        Assert.Equal(0, post.PhotoDiagnosticsUsed);
        Assert.Equal(0, post.SessionsStarted);
        Assert.Equal(1, post.DaysActive);
    }

    [Fact]
    public async Task DaysActive_counts_distinct_utc_dates()
    {
        await _store.IncrementAsync(
            StudentId, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        await _store.IncrementAsync(
            StudentId, EntitlementFeature.TutorTurn, Day1Later, CancellationToken.None);
        var post = await _store.IncrementAsync(
            StudentId, EntitlementFeature.TutorTurn, Day2, CancellationToken.None);
        Assert.Equal(3, post.TutorTurnsUsed);
        Assert.Equal(2, post.DaysActive);
    }

    [Fact]
    public async Task ResetAsync_zeroes_all_counters()
    {
        await _store.IncrementAsync(
            StudentId, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        await _store.IncrementAsync(
            StudentId, EntitlementFeature.PhotoDiagnostic, Day2, CancellationToken.None);
        await _store.ResetAsync(StudentId, CancellationToken.None);
        var post = await _store.GetAsync(StudentId, CancellationToken.None);
        Assert.Equal(StudentTrialConsumption.Empty, post);
    }

    [Fact]
    public async Task ResetAsync_is_idempotent_on_unseen_student()
    {
        await _store.ResetAsync("enc::never-seen", CancellationToken.None);
        // No exception expected.
    }

    [Fact]
    public async Task IncrementAsync_throws_on_blank_student_id()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.IncrementAsync(
                "   ", EntitlementFeature.TutorTurn, Day1, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_increments_do_not_lose_counts()
    {
        // 100 concurrent tasks each incrementing TutorTurn once. Total
        // post-condition should be exactly 100 (not less, not more).
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _store.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Day1, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);
        var post = await _store.GetAsync(StudentId, CancellationToken.None);
        Assert.Equal(100, post.TutorTurnsUsed);
    }

    // ----- Phase 1D-fix-2 item 2: atomic check-and-increment ------------

    [Fact]
    public async Task IncrementIfUnderCapAsync_under_cap_increments_and_returns_allowed()
    {
        var post = await _store.IncrementIfUnderCapAsync(
            StudentId, EntitlementFeature.TutorTurn,
            cap: 5, now: Day1, ct: CancellationToken.None);
        Assert.True(post.Allowed);
        Assert.Equal(1, post.Snapshot.TutorTurnsUsed);
    }

    [Fact]
    public async Task IncrementIfUnderCapAsync_at_cap_returns_disallowed_and_does_not_increment()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        }
        var post = await _store.IncrementIfUnderCapAsync(
            StudentId, EntitlementFeature.TutorTurn,
            cap: 5, now: Day1, ct: CancellationToken.None);
        Assert.False(post.Allowed);
        Assert.Equal(5, post.Snapshot.TutorTurnsUsed);
        var fresh = await _store.GetAsync(StudentId, CancellationToken.None);
        Assert.Equal(5, fresh.TutorTurnsUsed);
    }

    [Fact]
    public async Task IncrementIfUnderCapAsync_zero_cap_treated_as_unbounded()
    {
        for (var i = 0; i < 1000; i++)
        {
            var r = await _store.IncrementIfUnderCapAsync(
                StudentId, EntitlementFeature.TutorTurn,
                cap: 0, now: Day1, ct: CancellationToken.None);
            Assert.True(r.Allowed);
        }
        var fresh = await _store.GetAsync(StudentId, CancellationToken.None);
        Assert.Equal(1000, fresh.TutorTurnsUsed);
    }

    [Fact]
    public async Task IncrementIfUnderCapAsync_concurrent_callers_cannot_exceed_cap()
    {
        // 100 concurrent calls with cap=10. Exactly 10 must succeed; 90
        // must be rejected. Closes the TOCTOU window between filter check
        // and consumption-site increment. THIS TEST GUARDS THE PHASE
        // 1D-fix-2 ITEM 2 ATOMICITY GUARANTEE — failure means callers
        // can race past caps.
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _store.IncrementIfUnderCapAsync(
                StudentId, EntitlementFeature.TutorTurn,
                cap: 10, now: Day1, ct: CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(10, results.Count(r => r.Allowed));
        Assert.Equal(90, results.Count(r => !r.Allowed));
        var fresh = await _store.GetAsync(StudentId, CancellationToken.None);
        Assert.Equal(10, fresh.TutorTurnsUsed);
    }

    [Fact]
    public async Task IncrementIfUnderCapAsync_Generic_always_allowed_no_counter_change()
    {
        var post = await _store.IncrementIfUnderCapAsync(
            StudentId, EntitlementFeature.Generic,
            cap: 1, now: Day1, ct: CancellationToken.None);
        Assert.True(post.Allowed);
        Assert.Equal(0, post.Snapshot.TutorTurnsUsed);
        Assert.Equal(1, post.Snapshot.DaysActive);
    }
}
