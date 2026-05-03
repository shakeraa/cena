// =============================================================================
// Cena Platform — UnitEconomicsSnapshotDocument (EPIC-PRR-I PRR-330)
//
// Weekly unit-economics rollup snapshot, persisted so the admin dashboard's
// history chart can render trend lines across many weeks without re-scanning
// the entire subscription event log on every page load.
//
// Why a dedicated document:
//   - <see cref="UnitEconomicsAggregationService"/> scans event streams to
//     compute one <see cref="UnitEconomicsSnapshot"/>. At pilot scale that's
//     cheap; at 10k+ subscriptions the scan grows linearly with the window.
//   - The admin dashboard's history chart wants the last 12 weeks — twelve
//     scans per page load is a waste. The rollup worker computes each
//     week's snapshot ONCE at Sunday 06:00 UTC and upserts here;
//     subsequent reads are O(N rows) index lookups.
//   - <see cref="Id"/> is derived from the week-start Sunday date so the
//     worker is idempotent on retries (pod restart, transient transport
//     error) — a second Sunday run in the same week just rewrites the same
//     row with the same content.
//
// Honest-numbers contract (memory "Honest not complimentary"): we store the
// snapshot verbatim including raw counts + refunds + cancellations. The
// dashboard layer renders CIs/LTV estimates on top of these primitives; we
// do NOT strip sad-path fields (refunds, past-due) to prettify the history
// line. What was true at t0 stays true at t+12w.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Persisted copy of a weekly <see cref="UnitEconomicsSnapshot"/>, keyed by
/// the ISO Sunday-date of the window start. One row per week.
/// </summary>
/// <param name="Id">
/// Stable week identifier — format <c>"week-YYYY-MM-DD"</c> where the date is
/// the Sunday (UTC) that anchors the 7-day window. Same format produced by
/// <see cref="FormatWeekId(DateTimeOffset)"/> regardless of whether the
/// caller is the worker (computing the current week) or the endpoint
/// (looking up an older week).
/// </param>
/// <param name="WeekStartUtc">
/// The Sunday 00:00 UTC that the snapshot's window starts on. Redundant with
/// <see cref="Id"/> but cheaper to sort on than parsing the id string.
/// </param>
/// <param name="Snapshot">The full <see cref="UnitEconomicsSnapshot"/>.</param>
/// <param name="GeneratedAtUtc">
/// When the worker actually ran. This can be AFTER <see cref="WeekStartUtc"/>
/// by hours (worker fires Sunday 06:00 UTC; the window starts Sunday 00:00
/// UTC of the prior week). Useful for ops to detect delayed runs.
/// </param>
public sealed record UnitEconomicsSnapshotDocument(
    string Id,
    DateTimeOffset WeekStartUtc,
    UnitEconomicsSnapshot Snapshot,
    DateTimeOffset GeneratedAtUtc)
{
    /// <summary>
    /// Prefix for every week id. Exposed so query builders can filter the
    /// table without hard-coding the literal in multiple places.
    /// </summary>
    public const string IdPrefix = "week-";

    /// <summary>
    /// Format a week id from any instant — snaps to the Sunday that anchors
    /// the week the instant falls inside. Pure, deterministic; callers rely
    /// on this for idempotency (the worker uses the same snap as the
    /// endpoint so "is there already a row for this week" is a string
    /// comparison, not a date-range query).
    /// </summary>
    /// <param name="at">
    /// Any instant inside the target week (Sunday 00:00 UTC ≤ at &lt;
    /// following Sunday 00:00 UTC).
    /// </param>
    public static string FormatWeekId(DateTimeOffset at)
    {
        var weekStart = SnapToWeekStartUtc(at);
        return $"{IdPrefix}{weekStart.UtcDateTime:yyyy-MM-dd}";
    }

    /// <summary>
    /// Snap any instant to the Sunday 00:00 UTC that begins the week
    /// containing it. Pure function, exposed for tests and for the worker's
    /// window-math.
    /// </summary>
    public static DateTimeOffset SnapToWeekStartUtc(DateTimeOffset at)
    {
        var atUtc = at.ToUniversalTime();
        var midnightUtc = new DateTimeOffset(
            atUtc.Year, atUtc.Month, atUtc.Day, 0, 0, 0, TimeSpan.Zero);
        // C#'s DayOfWeek has Sunday=0, so days-since-Sunday = (int)DayOfWeek.
        var daysSinceSunday = (int)midnightUtc.DayOfWeek;
        return midnightUtc.AddDays(-daysSinceSunday);
    }
}
