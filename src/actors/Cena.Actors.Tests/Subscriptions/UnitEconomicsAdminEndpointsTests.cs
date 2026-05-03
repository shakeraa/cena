// =============================================================================
// Cena Platform — UnitEconomicsAdminEndpoints tests (EPIC-PRR-I PRR-330)
//
// Covers the endpoint-level invariants without standing up a full ASP.NET
// pipeline:
//   1. ClampWeeks — null/0/negative → default (12); overflow → cap (52);
//      legal value passes through.
//   2. HandleGetAsync returns the last N snapshots from the store, newest
//      first, and projects each to the wire DTO (week-id preserved, tier
//      rows flattened).
//   3. An empty store returns weeksReturned=0 but still reports the
//      requested count — no 404, no nulls.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Admin.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class UnitEconomicsAdminEndpointsTests
{
    // ── ClampWeeks ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, 12)]
    [InlineData(0, 12)]
    [InlineData(-5, 12)]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    [InlineData(52, 52)]
    [InlineData(100, 52)]
    [InlineData(int.MaxValue, 52)]
    public void ClampWeeks_clamps_into_valid_range(int? input, int expected)
    {
        Assert.Equal(expected, UnitEconomicsAdminEndpoints.ClampWeeks(input));
    }

    // ── HandleGetAsync with fake store ─────────────────────────────────────

    [Fact]
    public async Task HandleGetAsync_returns_snapshots_in_newest_first_order()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();
        await store.UpsertAsync(BuildDoc("2026-04-05"), CancellationToken.None);
        await store.UpsertAsync(BuildDoc("2026-04-19"), CancellationToken.None);
        await store.UpsertAsync(BuildDoc("2026-04-12"), CancellationToken.None);

        var result = await UnitEconomicsAdminEndpoints.HandleGetAsync(
            store: store,
            weeks: 10,
            ct: CancellationToken.None);

        var ok = Assert.IsType<Ok<UnitEconomicsHistoryResponseDto>>(result);
        var body = Assert.IsType<UnitEconomicsHistoryResponseDto>(ok.Value);
        Assert.Equal(10, body.WeeksRequested);
        Assert.Equal(3, body.WeeksReturned);
        Assert.Equal("week-2026-04-19", body.Weeks[0].WeekId);
        Assert.Equal("week-2026-04-12", body.Weeks[1].WeekId);
        Assert.Equal("week-2026-04-05", body.Weeks[2].WeekId);
    }

    [Fact]
    public async Task HandleGetAsync_clamps_weeks_to_max()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();
        await store.UpsertAsync(BuildDoc("2026-04-19"), CancellationToken.None);

        // Caller requests 9999; endpoint must clamp to MaxWeeks=52.
        var result = await UnitEconomicsAdminEndpoints.HandleGetAsync(
            store: store,
            weeks: 9999,
            ct: CancellationToken.None);

        var ok = Assert.IsType<Ok<UnitEconomicsHistoryResponseDto>>(result);
        var body = Assert.IsType<UnitEconomicsHistoryResponseDto>(ok.Value);
        Assert.Equal(UnitEconomicsAdminEndpoints.MaxWeeks, body.WeeksRequested);
        Assert.Equal(1, body.WeeksReturned);
    }

    [Fact]
    public async Task HandleGetAsync_on_empty_store_returns_zero_rows()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();

        var result = await UnitEconomicsAdminEndpoints.HandleGetAsync(
            store: store,
            weeks: null,
            ct: CancellationToken.None);

        var ok = Assert.IsType<Ok<UnitEconomicsHistoryResponseDto>>(result);
        var body = Assert.IsType<UnitEconomicsHistoryResponseDto>(ok.Value);
        Assert.Equal(UnitEconomicsAdminEndpoints.DefaultWeeks, body.WeeksRequested);
        Assert.Equal(0, body.WeeksReturned);
        Assert.Empty(body.Weeks);
    }

    [Fact]
    public void ToWireRow_projects_tier_snapshots_to_DTOs()
    {
        var doc = BuildDoc(
            "2026-04-19",
            tiers: new[]
            {
                new TierSnapshot(
                    Tier: SubscriptionTier.Premium,
                    ActiveSubscriptions: 42,
                    PastDueSubscriptions: 3,
                    CancelledInWindow: 1,
                    RefundedInWindow: 0,
                    RevenueAgorot: 1_045_800L,
                    RefundsAgorot: 0L),
            });

        var wire = UnitEconomicsAdminEndpoints.ToWireRow(doc);
        Assert.Equal("week-2026-04-19", wire.WeekId);
        Assert.Equal(42, wire.Totals.TotalActive);
        Assert.Equal(1_045_800L, wire.Totals.TotalRevenueAgorot);
        Assert.Single(wire.Totals.Rows);
        Assert.Equal("Premium", wire.Totals.Rows[0].TierId);
        Assert.Equal(42, wire.Totals.Rows[0].ActiveSubscriptions);
        Assert.Equal(3, wire.Totals.Rows[0].PastDueSubscriptions);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static UnitEconomicsSnapshotDocument BuildDoc(
        string sundayDateIso, IReadOnlyList<TierSnapshot>? tiers = null)
    {
        var weekStart = DateTimeOffset.Parse(sundayDateIso + "T00:00:00Z");
        var snapshot = new UnitEconomicsSnapshot(
            WindowStart: weekStart,
            WindowEnd: weekStart.AddDays(7),
            TierSnapshots: tiers ?? Array.Empty<TierSnapshot>());
        return new UnitEconomicsSnapshotDocument(
            Id: UnitEconomicsSnapshotDocument.FormatWeekId(weekStart),
            WeekStartUtc: weekStart,
            Snapshot: snapshot,
            GeneratedAtUtc: weekStart.AddHours(6));
    }
}
