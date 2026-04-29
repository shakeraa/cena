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
}
