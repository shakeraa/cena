// =============================================================================
// Cena Platform — UnitEconomicsRollupWorker tests (EPIC-PRR-I PRR-330)
//
// Covers the seams that ops + finance rely on:
//
//   1. Sunday 06:00 UTC scheduling (TimeUntilNextSundayMorning) — boundary
//      tests across Monday, Saturday 23:59, Sunday 05:00, Sunday 07:00,
//      never-negative floor. Mirrors WeeklyParentDigestWorker test style
//      because the bug class is identical (local-vs-UTC drift silently
//      bumping the cadence).
//   2. Idempotency: second RunOnceAsync for the same week is a no-op (the
//      aggregator is never asked to compute, the store is never upserted
//      again).
//   3. Margin-compression alert fires iff TWO consecutive weeks are below
//      threshold — single bad week does NOT alert.
//   4. Alert does not spuriously fire when prior-week data is missing
//      (fresh install, first rollup) — honest-not-complimentary means we
//      also don't alert-bomb on no-data.
//   5. Premium agorot-per-active math handles zero actives without
//      dividing by zero.
// =============================================================================

using Cena.Actors.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class UnitEconomicsRollupWorkerTests
{
    // ── TimeUntilNextSundayMorning — Sunday 06:00 UTC anchor ────────────────

    [Fact]
    public void Monday_schedules_for_next_Sunday_0600()
    {
        // 2026-04-27 is a Monday. Next Sunday 06:00 UTC = 2026-05-03 06:00.
        var now = new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 3, 6, 0, 0, TimeSpan.Zero) - now;
        Assert.Equal(expected, UnitEconomicsRollupWorker.TimeUntilNextSundayMorning(now));
    }

    [Fact]
    public void Sunday_before_0600_schedules_same_day_0600()
    {
        // 2026-05-03 is a Sunday. 05:00 UTC → schedule same-day 06:00.
        var now = new DateTimeOffset(2026, 5, 3, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(1),
            UnitEconomicsRollupWorker.TimeUntilNextSundayMorning(now));
    }

    [Fact]
    public void Sunday_at_or_after_0600_schedules_next_Sunday()
    {
        // 2026-05-03 is a Sunday. 07:00 UTC → next Sunday 2026-05-10 06:00.
        var now = new DateTimeOffset(2026, 5, 3, 7, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 10, 6, 0, 0, TimeSpan.Zero) - now;
        Assert.Equal(expected, UnitEconomicsRollupWorker.TimeUntilNextSundayMorning(now));
    }

    [Fact]
    public void Saturday_2359_schedules_sunday_morning()
    {
        // 2026-05-02 is a Saturday. 23:59 UTC → Sunday 2026-05-03 06:00 UTC.
        var now = new DateTimeOffset(2026, 5, 2, 23, 59, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 3, 6, 0, 0, TimeSpan.Zero) - now;
        Assert.Equal(expected, UnitEconomicsRollupWorker.TimeUntilNextSundayMorning(now));
    }

    [Fact]
    public void Never_returns_negative_timespan()
    {
        // Defensive — Task.Delay throws on negatives.
        var veryFarFuture = new DateTimeOffset(2100, 5, 3, 6, 0, 0, TimeSpan.Zero);
        var actual = UnitEconomicsRollupWorker.TimeUntilNextSundayMorning(veryFarFuture);
        Assert.True(actual > TimeSpan.Zero,
            $"TimeUntilNextSundayMorning returned non-positive span: {actual}");
    }

    [Fact]
    public void Non_UTC_input_snaps_against_UTC()
    {
        // Caller passes "Sunday 08:00 Asia/Jerusalem (+03:00)" which is
        // Sunday 05:00 UTC. Should return 1h (next anchor same-day 06:00).
        var asiaSunday8 = new DateTimeOffset(2026, 5, 3, 8, 0, 0, TimeSpan.FromHours(3));
        var actual = UnitEconomicsRollupWorker.TimeUntilNextSundayMorning(asiaSunday8);
        Assert.Equal(TimeSpan.FromHours(1), actual);
    }

    // ── Idempotency ────────────────────────────────────────────────────────

    [Fact]
    public async Task Second_run_same_week_skips_without_recomputing()
    {
        var sunday = new DateTimeOffset(2026, 4, 26, 6, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(sunday);
        var aggregator = new FakeAggregator();
        var store = new InMemoryUnitEconomicsSnapshotStore();

        var worker = BuildWorker(aggregator, store, clock);

        var first = await worker.RunOnceAsync(CancellationToken.None);
        Assert.False(first.SkippedBecauseExisting);
        Assert.Equal(1, aggregator.CallCount);

        // Second run — same clock instant, same week id.
        var second = await worker.RunOnceAsync(CancellationToken.None);
        Assert.True(second.SkippedBecauseExisting);
        Assert.Equal(1, aggregator.CallCount); // NOT recomputed.

        // Store still holds exactly one row.
        var all = await store.ListRecentAsync(10, CancellationToken.None);
        Assert.Single(all);
    }

    // ── Margin-compression alert ───────────────────────────────────────────

    [Fact]
    public void Alert_does_not_fire_on_single_bad_week()
    {
        // Current week below, prior week null → one bad week only.
        Assert.False(UnitEconomicsRollupWorker.EvaluateMarginCompressionAlert(
            currentPerActive: 1500L,   // < 2000 threshold
            priorPerActive: null,      // no prior data yet
            options: new UnitEconomicsRollupOptions()));
    }

    [Fact]
    public void Alert_does_not_fire_when_prior_week_above_threshold()
    {
        Assert.False(UnitEconomicsRollupWorker.EvaluateMarginCompressionAlert(
            currentPerActive: 1500L,   // < 2000 threshold
            priorPerActive: 2500L,     // prior week OK
            options: new UnitEconomicsRollupOptions()));
    }

    [Fact]
    public void Alert_fires_on_two_consecutive_bad_weeks()
    {
        Assert.True(UnitEconomicsRollupWorker.EvaluateMarginCompressionAlert(
            currentPerActive: 1500L,   // < 2000 threshold
            priorPerActive: 1800L,     // < 2000 threshold
            options: new UnitEconomicsRollupOptions()));
    }

    [Fact]
    public void Alert_respects_configured_threshold()
    {
        var options = new UnitEconomicsRollupOptions
        {
            PremiumMarginThresholdAgorotPerActive = 5_000L,
        };
        // Both weeks above default (2000) but below configured (5000).
        Assert.True(UnitEconomicsRollupWorker.EvaluateMarginCompressionAlert(
            currentPerActive: 3000L,
            priorPerActive: 2800L,
            options: options));
    }

    [Fact]
    public async Task Margin_compression_alert_fires_after_two_bad_weeks_end_to_end()
    {
        // Worker run at Sunday 2026-04-19 06:00 UTC rolls up the window
        // [2026-04-12, 2026-04-19) → week-id = "week-2026-04-12". Alert
        // comparison reads the prior week "week-2026-04-05" (the week
        // from 04-05 → 04-12). So we seed a below-threshold snapshot at
        // 04-05 and feed a below-threshold compute result for 04-12.
        var priorWeekStart = new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero);
        var currentWeekStart = priorWeekStart.AddDays(7); // 2026-04-12
        var runAt = currentWeekStart.AddDays(7).AddHours(6); // 2026-04-19 06:00 UTC

        var store = new InMemoryUnitEconomicsSnapshotStore();
        await store.UpsertAsync(new UnitEconomicsSnapshotDocument(
            Id: UnitEconomicsSnapshotDocument.FormatWeekId(priorWeekStart),
            WeekStartUtc: priorWeekStart,
            Snapshot: PremiumSnapshot(priorWeekStart, netAgorotPerActive: 1500L, actives: 10),
            GeneratedAtUtc: priorWeekStart.AddHours(6)), CancellationToken.None);

        var clock = new FakeClock(runAt);
        var aggregator = new FakeAggregator
        {
            NextSnapshot = PremiumSnapshot(currentWeekStart, netAgorotPerActive: 1200L, actives: 10),
        };
        var worker = BuildWorker(aggregator, store, clock);

        var outcome = await worker.RunOnceAsync(CancellationToken.None);

        Assert.False(outcome.SkippedBecauseExisting);
        Assert.True(outcome.AlertFired,
            "Two consecutive weeks below threshold should fire the alert.");
        Assert.Equal(1200L, outcome.PremiumAgorotPerActiveCurrentWeek);
        Assert.Equal(1500L, outcome.PremiumAgorotPerActivePriorWeek);
        Assert.Equal("week-2026-04-12", outcome.WeekId);
    }

    // ── Premium agorot-per-active math ─────────────────────────────────────

    [Fact]
    public void PremiumAgorotPerActive_returns_zero_when_no_actives()
    {
        var snap = new UnitEconomicsSnapshot(
            WindowStart: DateTimeOffset.UtcNow.AddDays(-7),
            WindowEnd: DateTimeOffset.UtcNow,
            TierSnapshots: new[]
            {
                new TierSnapshot(SubscriptionTier.Premium, 0, 0, 0, 0, 0, 0),
            });
        Assert.Equal(0L, UnitEconomicsRollupWorker.PremiumAgorotPerActive(snap));
    }

    [Fact]
    public void PremiumAgorotPerActive_subtracts_refunds_from_gross()
    {
        var snap = new UnitEconomicsSnapshot(
            WindowStart: DateTimeOffset.UtcNow.AddDays(-7),
            WindowEnd: DateTimeOffset.UtcNow,
            TierSnapshots: new[]
            {
                new TierSnapshot(
                    Tier: SubscriptionTier.Premium,
                    ActiveSubscriptions: 10,
                    PastDueSubscriptions: 0,
                    CancelledInWindow: 0,
                    RefundedInWindow: 2,
                    RevenueAgorot: 24_900L * 10, // 10 × ₪249
                    RefundsAgorot: 49_800L),     // 2 × ₪249 refunded
            });
        // Net = 249_000 − 49_800 = 199_200 agorot across 10 actives = 19_920 / active.
        Assert.Equal(19_920L, UnitEconomicsRollupWorker.PremiumAgorotPerActive(snap));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static UnitEconomicsRollupWorker BuildWorker(
        IUnitEconomicsAggregationService aggregator,
        IUnitEconomicsSnapshotStore store,
        TimeProvider clock) =>
        new(
            aggregator,
            store,
            clock,
            Options.Create(new UnitEconomicsRollupOptions()),
            NullLogger<UnitEconomicsRollupWorker>.Instance);

    private static UnitEconomicsSnapshot PremiumSnapshot(
        DateTimeOffset weekStart, long netAgorotPerActive, int actives)
    {
        // Synthesize a tier snapshot whose net-per-active equals the target.
        // Keep refunds zero so the worker's divide is exact.
        return new UnitEconomicsSnapshot(
            WindowStart: weekStart,
            WindowEnd: weekStart.AddDays(7),
            TierSnapshots: new[]
            {
                new TierSnapshot(
                    Tier: SubscriptionTier.Premium,
                    ActiveSubscriptions: actives,
                    PastDueSubscriptions: 0,
                    CancelledInWindow: 0,
                    RefundedInWindow: 0,
                    RevenueAgorot: netAgorotPerActive * actives,
                    RefundsAgorot: 0L),
            });
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeAggregator : IUnitEconomicsAggregationService
    {
        public int CallCount { get; private set; }
        public UnitEconomicsSnapshot? NextSnapshot { get; set; }

        public Task<UnitEconomicsSnapshot> ComputeAsync(
            DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
        {
            CallCount++;
            var snapshot = NextSnapshot ?? new UnitEconomicsSnapshot(
                WindowStart: windowStart,
                WindowEnd: windowEnd,
                TierSnapshots: Array.Empty<TierSnapshot>());
            return Task.FromResult(snapshot);
        }
    }
}
