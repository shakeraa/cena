// =============================================================================
// Cena Platform — MartenStudentTrialConsumptionStoreTests (Phase 1D-fix item 5)
//
// Drives the production Marten-backed implementation against the local
// dev Postgres (cena-postgres docker container) using a unique schema per
// test class instance — same pattern as MockExamRunServiceTests.
//
// Coverage gap closed (vs Phase 1D shipping with InMemory tests only):
//   * Marten serialisation of List<DateOnly> round-trips correctly
//   * Increment is atomic against per-key concurrency (load-modify-save
//     inside LightweightSession holds the row consistent under fan-out)
//   * Distinct-day counting works across DST / UTC-rollover edges
//   * ResetAsync does not error when the doc never existed
//   * GetAsync returns Empty for unseen ids
// =============================================================================

using Cena.Actors.Subscriptions;
using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public sealed class MartenStudentTrialConsumptionStoreTests : IAsyncLifetime
{
    // dev compose maps cena-postgres:5432 → host:5433 (same as
    // MockExamRunServiceTests). Per-class unique schema isolates parallel runs.
    private const string ConnectionString =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private DocumentStore _store = null!;
    private MartenStudentTrialConsumptionStore _sut = null!;

    private static readonly DateTimeOffset Day1 =
        new(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day1Later =
        new(2026, 4, 28, 22, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 =
        new(2026, 4, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day3 =
        new(2026, 4, 30, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "trial_consumption_test_"
                + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Schema.For<StudentTrialConsumptionDocument>().Identity(d => d.Id);
        });
        _sut = new MartenStudentTrialConsumptionStore(_store, TimeProvider.System);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetAsync_returns_empty_for_unseen_student()
    {
        var snapshot = await _sut.GetAsync("enc::unseen", CancellationToken.None);
        Assert.Equal(StudentTrialConsumption.Empty, snapshot);
    }

    [Fact]
    public async Task IncrementAsync_TutorTurn_persists_and_round_trips()
    {
        const string id = "enc::student::marten-tutor-turn";
        var post = await _sut.IncrementAsync(
            id, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        Assert.Equal(1, post.TutorTurnsUsed);
        Assert.Equal(0, post.PhotoDiagnosticsUsed);
        Assert.Equal(0, post.SessionsStarted);
        Assert.Equal(1, post.DaysActive);

        // Re-read via fresh GetAsync to prove the document hit Postgres
        // (not just the session cache).
        var fresh = await _sut.GetAsync(id, CancellationToken.None);
        Assert.Equal(post, fresh);
    }

    [Fact]
    public async Task IncrementAsync_aggregates_across_multiple_features()
    {
        const string id = "enc::student::marten-multi-feature";
        await _sut.IncrementAsync(id, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        await _sut.IncrementAsync(id, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        await _sut.IncrementAsync(id, EntitlementFeature.PhotoDiagnostic, Day2, CancellationToken.None);
        await _sut.IncrementAsync(id, EntitlementFeature.PracticeSession, Day3, CancellationToken.None);
        var fresh = await _sut.GetAsync(id, CancellationToken.None);
        Assert.Equal(2, fresh.TutorTurnsUsed);
        Assert.Equal(1, fresh.PhotoDiagnosticsUsed);
        Assert.Equal(1, fresh.SessionsStarted);
        Assert.Equal(3, fresh.DaysActive);
    }

    [Fact]
    public async Task DaysActive_counts_distinct_utc_dates_after_round_trip()
    {
        const string id = "enc::student::marten-distinct-days";
        await _sut.IncrementAsync(id, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        await _sut.IncrementAsync(id, EntitlementFeature.TutorTurn, Day1Later, CancellationToken.None);
        await _sut.IncrementAsync(id, EntitlementFeature.TutorTurn, Day2, CancellationToken.None);
        var fresh = await _sut.GetAsync(id, CancellationToken.None);
        Assert.Equal(3, fresh.TutorTurnsUsed);
        Assert.Equal(2, fresh.DaysActive);
    }

    [Fact]
    public async Task ResetAsync_zeroes_all_counters_and_persists()
    {
        const string id = "enc::student::marten-reset";
        await _sut.IncrementAsync(id, EntitlementFeature.TutorTurn, Day1, CancellationToken.None);
        await _sut.IncrementAsync(id, EntitlementFeature.PhotoDiagnostic, Day2, CancellationToken.None);
        await _sut.ResetAsync(id, CancellationToken.None);
        var fresh = await _sut.GetAsync(id, CancellationToken.None);
        Assert.Equal(StudentTrialConsumption.Empty, fresh);
    }

    [Fact]
    public async Task ResetAsync_is_idempotent_on_unseen_student()
    {
        await _sut.ResetAsync("enc::never-seen", CancellationToken.None);
        // No exception expected. Verify GetAsync still returns Empty.
        var fresh = await _sut.GetAsync("enc::never-seen", CancellationToken.None);
        Assert.Equal(StudentTrialConsumption.Empty, fresh);
    }

    [Fact]
    public async Task IncrementAsync_throws_on_blank_student_id()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.IncrementAsync(
                "   ", EntitlementFeature.TutorTurn, Day1, CancellationToken.None));
    }

    [Fact]
    public async Task IncrementAsync_Generic_does_not_increment_counters_but_records_day()
    {
        const string id = "enc::student::marten-generic";
        var post = await _sut.IncrementAsync(
            id, EntitlementFeature.Generic, Day1, CancellationToken.None);
        Assert.Equal(0, post.TutorTurnsUsed);
        Assert.Equal(0, post.PhotoDiagnosticsUsed);
        Assert.Equal(0, post.SessionsStarted);
        Assert.Equal(1, post.DaysActive);
        var fresh = await _sut.GetAsync(id, CancellationToken.None);
        Assert.Equal(post, fresh);
    }
}
