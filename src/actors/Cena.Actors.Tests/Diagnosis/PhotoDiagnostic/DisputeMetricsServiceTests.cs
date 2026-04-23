// =============================================================================
// Cena Platform — DisputeMetricsService tests (EPIC-PRR-J PRR-393)
//
// Locks the thin service layer: repo → aggregator → snapshot plumbing.
// Uses InMemoryDiagnosticDisputeRepository (the same fixture the retention
// worker tests use) so we exercise the real repository contract, not an
// NSubstitute-only surface.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DisputeMetricsServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_zero_snapshot_when_no_disputes()
    {
        var repo = new InMemoryDiagnosticDisputeRepository();
        var service = new MartenDisputeMetricsService(
            repo, new FixedClock(Now), NullLogger<MartenDisputeMetricsService>.Instance);

        var snap = await service.GetAsync(AggregationWindow.SevenDay, CancellationToken.None);

        Assert.Equal(0, snap.TotalDisputes);
        Assert.False(snap.IsAboveAlertThreshold);
    }

    [Fact]
    public async Task GetAsync_folds_repository_rows_through_aggregator()
    {
        var repo = new InMemoryDiagnosticDisputeRepository();
        // 1 upheld + 19 rejected in-window → 5% exactly → alert fires.
        await repo.InsertAsync(MakeDoc("d-upheld", Now.AddDays(-1), DisputeStatus.Upheld), CancellationToken.None);
        for (var i = 0; i < 19; i++)
        {
            await repo.InsertAsync(
                MakeDoc($"d-rej-{i}", Now.AddDays(-1), DisputeStatus.Rejected),
                CancellationToken.None);
        }

        var service = new MartenDisputeMetricsService(
            repo, new FixedClock(Now), NullLogger<MartenDisputeMetricsService>.Instance);

        var snap = await service.GetAsync(AggregationWindow.SevenDay, CancellationToken.None);

        Assert.Equal(20, snap.TotalDisputes);
        Assert.Equal(1, snap.UpheldCount);
        Assert.Equal(19, snap.RejectedCount);
        Assert.Equal(0.05, snap.UpheldRate, 9);
        Assert.True(snap.IsAboveAlertThreshold);
    }

    [Fact]
    public async Task GetAsync_thirty_day_window_returns_thirty_day_shape()
    {
        var repo = new InMemoryDiagnosticDisputeRepository();
        await repo.InsertAsync(
            MakeDoc("d-eight", Now.AddDays(-8), DisputeStatus.Upheld), CancellationToken.None);
        var service = new MartenDisputeMetricsService(
            repo, new FixedClock(Now), NullLogger<MartenDisputeMetricsService>.Instance);

        var seven = await service.GetAsync(AggregationWindow.SevenDay, CancellationToken.None);
        var thirty = await service.GetAsync(AggregationWindow.ThirtyDay, CancellationToken.None);

        Assert.Equal(7, seven.WindowDays);
        Assert.Equal(0, seven.TotalDisputes);
        Assert.Equal(30, thirty.WindowDays);
        Assert.Equal(1, thirty.TotalDisputes);
    }

    [Fact]
    public async Task NoopDisputeMetricsService_returns_empty_snapshot_for_any_window()
    {
        var service = new NoopDisputeMetricsService();

        var seven = await service.GetAsync(AggregationWindow.SevenDay, CancellationToken.None);
        var thirty = await service.GetAsync(AggregationWindow.ThirtyDay, CancellationToken.None);

        Assert.Equal(0, seven.TotalDisputes);
        Assert.Equal(7, seven.WindowDays);
        Assert.Equal(0, thirty.TotalDisputes);
        Assert.Equal(30, thirty.WindowDays);
        Assert.False(seven.IsAboveAlertThreshold);
    }

    private static DiagnosticDisputeDocument MakeDoc(
        string id, DateTimeOffset submittedAt, DisputeStatus status) => new()
        {
            Id = id,
            DiagnosticId = "diag-" + id,
            StudentSubjectIdHash = "hash-" + id,
            Reason = DisputeReason.WrongNarration,
            StudentComment = null,
            Status = status,
            SubmittedAt = submittedAt,
            ReviewedAt = status is DisputeStatus.New or DisputeStatus.InReview
                ? null
                : submittedAt.AddMinutes(5),
            ReviewerNote = null,
        };

    // Minimal TimeProvider fixture. Using a custom subclass rather than
    // FakeTimeProvider to avoid adding a new package reference.
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
