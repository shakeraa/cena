// =============================================================================
// Cena Platform — TaxonomyReviewReportWorker tests (EPIC-PRR-J PRR-392)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class TaxonomyReviewReportWorkerTests
{
    // ── Scheduling kernel ─────────────────────────────────────────────────

    [Fact]
    public void Monday_before_07_schedules_same_day_07_UTC()
    {
        // 2026-05-04 is a Monday. 06:00 UTC → schedule same-day 07:00.
        var now = new DateTimeOffset(2026, 5, 4, 6, 0, 0, TimeSpan.Zero);
        var actual = TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(now, 7);
        Assert.Equal(TimeSpan.FromHours(1), actual);
    }

    [Fact]
    public void Monday_at_or_after_07_schedules_next_Monday()
    {
        // 2026-05-04 is a Monday. 08:00 UTC → next Monday 2026-05-11 07:00.
        var now = new DateTimeOffset(2026, 5, 4, 8, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 11, 7, 0, 0, TimeSpan.Zero) - now;
        var actual = TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(now, 7);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Wednesday_schedules_for_next_Monday()
    {
        // 2026-05-06 is a Wednesday. Next Monday 2026-05-11 07:00.
        var now = new DateTimeOffset(2026, 5, 6, 14, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 11, 7, 0, 0, TimeSpan.Zero) - now;
        var actual = TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(now, 7);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Sunday_2359_schedules_next_morning()
    {
        // 2026-05-03 Sunday 23:59 → 2026-05-04 07:00 UTC.
        var now = new DateTimeOffset(2026, 5, 3, 23, 59, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 4, 7, 0, 0, TimeSpan.Zero) - now;
        var actual = TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(now, 7);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Never_returns_negative_timespan()
    {
        var farFuture = new DateTimeOffset(2100, 5, 4, 7, 0, 0, TimeSpan.Zero);
        var actual = TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(farFuture, 7);
        Assert.True(actual > TimeSpan.Zero);
    }

    [Fact]
    public void Rejects_out_of_range_hour()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(now, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaxonomyReviewReportWorker.TimeUntilNextMondayMorning(now, -1));
    }

    // ── Flag dispatch ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunOnceAsync_returns_count_from_governance_service()
    {
        var service = new FakeGovernanceService(
            flaggedKeys: new[] { "template-a", "template-b", "template-c" });
        var worker = new TaxonomyReviewReportWorker(
            service,
            TimeProvider.System,
            TaxonomyReviewReportOptions.Default,
            NullLogger<TaxonomyReviewReportWorker>.Instance);

        var count = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal(TaxonomyReviewReportOptions.Default.DisputeUpheldRateThreshold,
            service.LastThreshold);
    }

    [Fact]
    public async Task RunOnceAsync_empty_flagged_returns_zero()
    {
        var service = new FakeGovernanceService(flaggedKeys: Array.Empty<string>());
        var worker = new TaxonomyReviewReportWorker(
            service,
            TimeProvider.System,
            TaxonomyReviewReportOptions.Default,
            NullLogger<TaxonomyReviewReportWorker>.Instance);

        var count = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RunOnceAsync_honors_custom_threshold()
    {
        var service = new FakeGovernanceService(flaggedKeys: Array.Empty<string>());
        var custom = new TaxonomyReviewReportOptions(
            DisputeUpheldRateThreshold: 0.10,
            WeeklyHourUtc: 7);
        var worker = new TaxonomyReviewReportWorker(
            service, TimeProvider.System, custom,
            NullLogger<TaxonomyReviewReportWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0.10, service.LastThreshold);
    }

    [Fact]
    public void Null_deps_throw()
    {
        var service = new FakeGovernanceService(Array.Empty<string>());
        Assert.Throws<ArgumentNullException>(() => new TaxonomyReviewReportWorker(
            null!, TimeProvider.System,
            TaxonomyReviewReportOptions.Default,
            NullLogger<TaxonomyReviewReportWorker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new TaxonomyReviewReportWorker(
            service, null!,
            TaxonomyReviewReportOptions.Default,
            NullLogger<TaxonomyReviewReportWorker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new TaxonomyReviewReportWorker(
            service, TimeProvider.System, null!,
            NullLogger<TaxonomyReviewReportWorker>.Instance));
        Assert.Throws<ArgumentNullException>(() => new TaxonomyReviewReportWorker(
            service, TimeProvider.System,
            TaxonomyReviewReportOptions.Default, null!));
    }

    // ── Fake ──────────────────────────────────────────────────────────────

    private sealed class FakeGovernanceService : ITaxonomyGovernanceService
    {
        private readonly IReadOnlyList<string> _flagged;
        public double LastThreshold { get; private set; }

        public FakeGovernanceService(IReadOnlyList<string> flaggedKeys)
        {
            _flagged = flaggedKeys;
        }

        public Task<IReadOnlyList<string>> FlagHighDisputeTemplatesAsync(
            double threshold, CancellationToken ct)
        {
            LastThreshold = threshold;
            return Task.FromResult(_flagged);
        }

        public Task<TaxonomyVersionDocument> RecordReviewAsync(
            string templateKey, string reviewer, bool approve, CancellationToken ct)
        {
            // Not used by these tests.
            throw new NotImplementedException();
        }
    }
}
