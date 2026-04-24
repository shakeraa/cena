// =============================================================================
// Cena Platform — MartenRefundUsageProbe (EPIC-PRR-I PRR-306 prod binding)
//
// Production implementation of IRefundUsageProbe. Sums household usage
// across every linked student during the refund window by composing two
// existing seams:
//
//   1. IPhotoDiagnosticMonthlyUsage (PRR-400) — per-student calendar-month
//      counter already Marten-backed. We iterate each month that overlaps
//      the refund window and accumulate.
//
//   2. Marten event store — HintRequested_V1 events are appended by the
//      StudentActor. We query raw events with the IEvent wrapper so we
//      have the append-timestamp (the event payload itself carries no
//      timestamp field), then filter to the window and the linked-student
//      set client-side. Event volume per household over a 30-day window
//      is bounded (dozens-to-hundreds, not millions), so a single scan
//      + client-side filter beats building a new projection for a rarely-
//      triggered eligibility probe.
//
// Why compose instead of a single projection: the abuse thresholds are
// deliberately far above legitimate usage (500 diagnostics, 50 hints)
// so this probe fires on the refund-request path only — not the hot
// path. A projection would be over-engineering; reading two existing
// seams is the right complexity ceiling here.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Events;
using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Production <see cref="IRefundUsageProbe"/>. Reads the Marten-backed
/// photo-diagnostic monthly counter + scans the Marten event log for
/// hint requests in the refund window.
/// </summary>
public sealed class MartenRefundUsageProbe : IRefundUsageProbe
{
    private readonly IDocumentStore _store;
    private readonly IPhotoDiagnosticMonthlyUsage _photoUsage;

    /// <summary>Construct with the document store and the photo usage counter.</summary>
    public MartenRefundUsageProbe(
        IDocumentStore store, IPhotoDiagnosticMonthlyUsage photoUsage)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _photoUsage = photoUsage ?? throw new ArgumentNullException(nameof(photoUsage));
    }

    /// <inheritdoc />
    public async Task<RefundUsageCounts> GetAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linkedStudents);
        if (linkedStudents.Count == 0) return RefundUsageCounts.Zero;

        // Diagnostics: iterate each calendar-month touching the window.
        // The photo counter key is per (student, YYYY-MM); the month
        // resolution is intentionally coarse because the PRR-400 caps are
        // also monthly. A 30-day window may touch 1 or 2 months depending
        // on activation date, so this loop runs at most 2 × |students|
        // lookups per refund request — well within budget.
        long diagnostics = 0;
        var cursor = new DateTimeOffset(
            windowStartUtc.Year, windowStartUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEndFloor = new DateTimeOffset(
            windowEndUtc.Year, windowEndUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        while (cursor <= windowEndFloor)
        {
            foreach (var student in linkedStudents)
            {
                if (string.IsNullOrWhiteSpace(student.StudentSubjectIdEncrypted))
                {
                    continue;
                }
                diagnostics += await _photoUsage
                    .GetAsync(student.StudentSubjectIdEncrypted, cursor, ct)
                    .ConfigureAwait(false);
            }
            cursor = cursor.AddMonths(1);
        }

        // Hints: query raw events (wrapped) so we retain the append
        // timestamp, filter to the refund window, then count events whose
        // payload StudentId matches any of the linked students. The event
        // payload carries no timestamp field (see PedagogyEvents.cs) so
        // IEvent.Timestamp is the authoritative clock.
        var linkedIds = new HashSet<string>(
            linkedStudents.Select(s => s.StudentSubjectIdEncrypted),
            StringComparer.Ordinal);

        await using var session = _store.QuerySession();
        var hintEvents = await session.Events
            .QueryRawEventDataOnly<HintRequested_V1>()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        // QueryRawEventDataOnly returns the payload without the wrapper;
        // fall back to a secondary query for the matching IEvent records
        // to recover timestamps. Two scans of the same table is acceptable
        // for the off-hot-path refund request — correctness > latency.
        var timedHints = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "hint_requested_v1"
                     || e.EventTypeName == "HintRequested_V1"
                     || e.EventTypeName == "hint_requested_V1")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        long hints = 0;
        foreach (var ev in timedHints)
        {
            if (ev.Timestamp < windowStartUtc || ev.Timestamp > windowEndUtc)
            {
                continue;
            }
            if (ev.Data is HintRequested_V1 h && linkedIds.Contains(h.StudentId))
            {
                hints++;
            }
        }
        // Cross-check: QueryRawEventDataOnly returned the payloads; if the
        // QueryAllRawEvents type-name predicate missed the canonical name
        // (Marten's alias scheme has varied across versions) we would see
        // hintEvents.Count > 0 but timedHints.Count == 0. In that rare
        // case we fall back to the payload-only scan with no window
        // filtering — conservative for abuse detection (may over-count,
        // which errs toward denying a genuinely-abusive request).
        if (timedHints.Count == 0 && hintEvents.Count > 0)
        {
            hints = hintEvents.Count(h => linkedIds.Contains(h.StudentId));
        }

        return new RefundUsageCounts(diagnostics, hints);
    }
}
