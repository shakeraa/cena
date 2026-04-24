// =============================================================================
// Cena Platform — UnitEconomicsSnapshotStore + Document tests (EPIC-PRR-I PRR-330)
//
// Pins the primitives the rollup worker + admin endpoint rely on:
//
//   1. The week-id format is stable "week-YYYY-MM-DD" with the Sunday date.
//      Idempotency on retries depends on this; any drift silently creates
//      duplicate rows.
//   2. Snap-to-Sunday round-trips: an instant on Tuesday resolves to the
//      same week id as an instant on Friday of the same week.
//   3. InMemory store: upsert → list → get round-trip.
//   4. InMemory store: second upsert for the same week id overwrites — the
//      idempotent-retry guarantee.
//   5. InMemory store: ListRecentAsync clamps takeWeeks + orders newest-first.
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class UnitEconomicsSnapshotStoreTests
{
    // ── Week-id format + snap-to-Sunday ─────────────────────────────────────

    [Fact]
    public void FormatWeekId_returns_week_prefix_with_Sunday_date()
    {
        // 2026-04-19 is a Sunday.
        var sundayMorning = new DateTimeOffset(2026, 4, 19, 6, 30, 0, TimeSpan.Zero);
        Assert.Equal("week-2026-04-19", UnitEconomicsSnapshotDocument.FormatWeekId(sundayMorning));
    }

    [Theory]
    [InlineData("2026-04-19T00:00:00Z")] // Sunday 00:00 → that Sunday
    [InlineData("2026-04-19T12:00:00Z")] // Sunday noon   → that Sunday
    [InlineData("2026-04-21T09:00:00Z")] // Tuesday       → prior Sunday 04-19
    [InlineData("2026-04-25T23:59:00Z")] // Saturday late → same Sunday 04-19
    public void Any_weekday_snaps_to_the_Sunday_anchor(string iso)
    {
        var at = DateTimeOffset.Parse(iso);
        Assert.Equal("week-2026-04-19", UnitEconomicsSnapshotDocument.FormatWeekId(at));
    }

    [Fact]
    public void SnapToWeekStartUtc_returns_midnight_on_Sunday()
    {
        var wed = new DateTimeOffset(2026, 4, 22, 14, 30, 17, TimeSpan.Zero);
        var snap = UnitEconomicsSnapshotDocument.SnapToWeekStartUtc(wed);
        Assert.Equal(new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.Zero), snap);
        Assert.Equal(DayOfWeek.Sunday, snap.DayOfWeek);
    }

    [Fact]
    public void SnapToWeekStartUtc_handles_local_offset_inputs()
    {
        // A caller passing DateTimeOffset with a non-UTC offset must still
        // snap against UTC. Otherwise "Sunday 03:00 Asia/Jerusalem" (which
        // is Saturday 00:00 UTC) would resolve to the WRONG week.
        var asiaSunday = new DateTimeOffset(2026, 4, 19, 3, 0, 0, TimeSpan.FromHours(3));
        // = 2026-04-19 00:00 UTC = Sunday 00:00 UTC → same week.
        var snap = UnitEconomicsSnapshotDocument.SnapToWeekStartUtc(asiaSunday);
        Assert.Equal(new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.Zero), snap);
    }

    // ── InMemory store round-trip ───────────────────────────────────────────

    [Fact]
    public async Task Upsert_then_Get_returns_the_row()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();
        var doc = BuildDoc("2026-04-19");
        await store.UpsertAsync(doc, CancellationToken.None);

        var fetched = await store.GetAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(doc.Id, fetched!.Id);
        Assert.Equal(doc.WeekStartUtc, fetched.WeekStartUtc);
        Assert.Equal(doc.GeneratedAtUtc, fetched.GeneratedAtUtc);
    }

    [Fact]
    public async Task Upsert_same_week_id_overwrites()
    {
        // This is the idempotency guarantee the rollup worker relies on.
        // If it retries after a transient DB blip, the second run MUST NOT
        // create a second row — it must rewrite the first.
        var store = new InMemoryUnitEconomicsSnapshotStore();
        var first = BuildDoc("2026-04-19", generatedOffsetMinutes: 0);
        var second = BuildDoc("2026-04-19", generatedOffsetMinutes: 60);

        await store.UpsertAsync(first, CancellationToken.None);
        await store.UpsertAsync(second, CancellationToken.None);

        var list = await store.ListRecentAsync(10, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(second.GeneratedAtUtc, list[0].GeneratedAtUtc);
    }

    [Fact]
    public async Task ListRecentAsync_returns_newest_first_and_clamps()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();
        // Insert 5 weeks out of order.
        await store.UpsertAsync(BuildDoc("2026-04-05"), CancellationToken.None);
        await store.UpsertAsync(BuildDoc("2026-04-19"), CancellationToken.None);
        await store.UpsertAsync(BuildDoc("2026-04-12"), CancellationToken.None);
        await store.UpsertAsync(BuildDoc("2026-03-29"), CancellationToken.None);
        await store.UpsertAsync(BuildDoc("2026-04-26"), CancellationToken.None);

        var latest3 = await store.ListRecentAsync(3, CancellationToken.None);
        Assert.Equal(3, latest3.Count);
        Assert.Equal("week-2026-04-26", latest3[0].Id);
        Assert.Equal("week-2026-04-19", latest3[1].Id);
        Assert.Equal("week-2026-04-12", latest3[2].Id);
    }

    [Fact]
    public async Task ListRecentAsync_with_nonpositive_take_returns_empty()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();
        await store.UpsertAsync(BuildDoc("2026-04-19"), CancellationToken.None);
        Assert.Empty(await store.ListRecentAsync(0, CancellationToken.None));
        Assert.Empty(await store.ListRecentAsync(-1, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_for_unknown_week_returns_null()
    {
        var store = new InMemoryUnitEconomicsSnapshotStore();
        Assert.Null(await store.GetAsync("week-2030-01-05", CancellationToken.None));
        Assert.Null(await store.GetAsync("", CancellationToken.None));
        Assert.Null(await store.GetAsync("   ", CancellationToken.None));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static UnitEconomicsSnapshotDocument BuildDoc(
        string sundayDateIso, int generatedOffsetMinutes = 30)
    {
        var weekStart = DateTimeOffset.Parse(sundayDateIso + "T00:00:00Z");
        var generatedAt = weekStart.AddMinutes(generatedOffsetMinutes + 6 * 60); // Sunday 06:00+
        var snapshot = new UnitEconomicsSnapshot(
            WindowStart: weekStart,
            WindowEnd: weekStart.AddDays(7),
            TierSnapshots: Array.Empty<TierSnapshot>());
        return new UnitEconomicsSnapshotDocument(
            Id: UnitEconomicsSnapshotDocument.FormatWeekId(weekStart),
            WeekStartUtc: weekStart,
            Snapshot: snapshot,
            GeneratedAtUtc: generatedAt);
    }
}
