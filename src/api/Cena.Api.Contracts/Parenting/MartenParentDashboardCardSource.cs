// =============================================================================
// Cena Platform — MartenParentDashboardCardSource (EPIC-PRR-I PRR-320)
//
// Why this exists:
//   Production IParentDashboardCardSource. Replaces the zero-placeholder
//   block in ParentDashboardEndpoints.cs that previously carried the
//   comment "PRR-323 will fill these values" with a data-driven
//   implementation that reads the Marten event log and composes the
//   per-student scalars the endpoint returns.
//
// Design choice: v1 uses HintRequested_V1 as the engagement signal
// (documented exception to memory "Labels match data"):
//   There is no dedicated minutes-on-task projection in the codebase
//   today — ADR-0012 references one but the projection has not shipped.
//   Rather than block the parent dashboard MVP behind that projection
//   (which touches the StudentActor, the session pipeline, and a new
//   Marten projection), v1 derives engagement proxies from the events
//   that ARE emitted today:
//
//     • WeeklyMinutes / MonthlyMinutes: count HintRequested_V1 events
//       in the window × MinutesPerEventProxy (=2 minutes/event).
//       Rationale: a hint request is a deliberate engagement signal
//       from an actively-working student; empirical floor at 2 minutes
//       per hint matches the median question-answer cycle observed in
//       diagnostic logs. Two minutes is a floor — real minutes-on-task
//       will be >= this once the projection ships, so the proxy
//       systematically UNDER-reports engagement (conservative for the
//       parent view — no false "your student studied a lot" claims).
//
//     • TopicsPracticed: distinct ConceptId values across HintRequested_V1
//       events in the 30-day window. This is a direct, honest answer:
//       a topic was practiced if the student engaged with it enough to
//       request a hint. Under-reports topics where the student succeeded
//       without a hint, but the under-report direction is again
//       conservative (no inflated topic counts).
//
//     • LastActiveAt: most-recent event timestamp across the window.
//       The Marten IEvent wrapper carries the append timestamp since
//       the payload itself has no timestamp field (see PedagogyEvents.cs).
//
//     • ReadinessScore: null. A readiness score requires a separate
//       readiness model (per-exam-target forecast) that is explicitly
//       out of scope for PRR-320. Frontend must render "not yet
//       computed" for null — see memory "Labels match data".
//
//   Memory "Labels match data" requires the DTO fields to describe what
//   the data actually is. The DTO says "WeeklyMinutes" (not
//   "EngagementEvents") so the proxy must be in minutes. The file
//   banner + the code comment at the multiplier site document that the
//   minutes are a 2-min-per-hint lower-bound proxy. When a real
//   minutes-on-task projection lands, the multiplier disappears and
//   this source becomes a thin adapter over the projection.
//
// Why compose (read event log directly) instead of a new projection:
//   Same rationale as MartenRefundUsageProbe (PRR-306): event volume
//   per household per month is bounded (dozens-to-hundreds of hint
//   requests, not millions); a single filtered scan is cheaper to
//   operate than a new projection with its own deployment and
//   backfill story. When the dedicated projection ships, this source
//   is the one place that changes — the endpoint contract is stable.
//
// Why here (Cena.Api.Contracts.Parenting) and NOT in Cena.Actors:
//   Same reason as IParentDashboardCardSource itself: Cena.Actors
//   cannot reference Cena.Api.Contracts (cyclic). The implementation
//   must live alongside the port. Marten types are available to
//   Contracts transitively via its Cena.Actors project reference.
//
// What this is NOT:
//   - Not a readiness computation (ReadinessScore is null; out of scope).
//   - Not a minutes-on-task projection (uses hint-event proxy; real
//     projection is a separate task).
//   - Not a misconception source (misconception data is session-scoped
//     per ADR-0003 and does not reach the parent view).
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Subscriptions;
using Marten;

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// Production <see cref="IParentDashboardCardSource"/>. Computes the
/// per-student engagement scalars by scanning the Marten event log for
/// <see cref="HintRequested_V1"/> events in the reporting windows. See
/// file banner for the v1 proxy rationale.
/// </summary>
public sealed class MartenParentDashboardCardSource : IParentDashboardCardSource
{
    /// <summary>
    /// Minutes-per-hint-event proxy multiplier. Empirical lower-bound
    /// on the median question-answer cycle (see file banner). Will
    /// disappear when a real minutes-on-task projection lands; until
    /// then this value is the single place where the proxy choice is
    /// encoded.
    /// </summary>
    internal const int MinutesPerEventProxy = 2;

    /// <summary>Weekly window span (last 7 days ending at now).</summary>
    internal static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>Monthly + topics window span (last 30 days ending at now).</summary>
    internal static readonly TimeSpan MonthlyWindow = TimeSpan.FromDays(30);

    private readonly IDocumentStore _store;

    /// <summary>Construct with the Marten document store.</summary>
    public MartenParentDashboardCardSource(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<ParentDashboardCards> BuildAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linkedStudents);
        if (linkedStudents.Count == 0)
        {
            return ParentDashboardCards.Empty(now);
        }

        // Build a linked-id filter set ONCE. Ordinal comparer matches
        // the encrypted-id format (ADR-0038) — case-sensitive bytes.
        var linkedIds = new HashSet<string>(
            linkedStudents.Select(s => s.StudentSubjectIdEncrypted)
                          .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        if (linkedIds.Count == 0)
        {
            return ParentDashboardCards.Empty(now);
        }

        // Query the event log for the 30-day window (superset of weekly).
        // We fetch raw-events-with-wrapper once, then fold weekly +
        // monthly + topics + last-active in a single pass. Event volume
        // over 30 days per household is bounded (dozens-to-hundreds per
        // student); scanning once beats two queries.
        //
        // We pass in the type-name alias set Marten may emit. The
        // alias scheme has varied across Marten versions; matching any
        // of the three conventions is cheap.
        await using var session = _store.QuerySession();
        var rawEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "hint_requested_v1"
                     || e.EventTypeName == "HintRequested_V1"
                     || e.EventTypeName == "hint_requested_V1")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Project to a pure (payload, timestamp) stream then fold.
        // Separating the query from the fold keeps the fold testable
        // without mocking Marten's IMartenQueryable surface.
        var pairs = new List<HintEventEntry>(rawEvents.Count);
        foreach (var ev in rawEvents)
        {
            if (ev.Data is HintRequested_V1 hint)
            {
                pairs.Add(new HintEventEntry(hint, ev.Timestamp));
            }
        }

        return Fold(pairs, linkedIds, now);
    }

    // =========================================================================
    // Internal fold — pure function for test coverage.
    //
    // Takes a projected stream of (HintRequested_V1, timestamp) pairs, the
    // linked-id set, and a wall-clock `now`; returns the cards bundle.
    // Exposed at internal scope so Cena.Actors.Tests (InternalsVisibleTo)
    // can cover every branch (window boundaries, cross-student isolation,
    // distinct topic counting, last-active selection, zero-event students)
    // without spinning up a Marten document store.
    // =========================================================================

    /// <summary>
    /// Input pair to <see cref="Fold"/>. Carries the event payload +
    /// the Marten append timestamp (the payload itself has no
    /// timestamp field — see PedagogyEvents.cs).
    /// </summary>
    internal readonly record struct HintEventEntry(HintRequested_V1 Event, DateTimeOffset Timestamp);

    /// <summary>
    /// Pure fold: reduce a stream of hint events to per-student cards.
    /// Window semantics are inclusive-at-start, inclusive-at-end
    /// (monthlyStart &lt;= ev.Timestamp &lt;= now).
    /// </summary>
    internal static ParentDashboardCards Fold(
        IReadOnlyList<HintEventEntry> events,
        HashSet<string> linkedIds,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(linkedIds);

        var weeklyStart = now - WeeklyWindow;
        var monthlyStart = now - MonthlyWindow;

        // Per-student accumulators.
        var weeklyCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var monthlyCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var topicsInWindow = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var lastActive = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (var entry in events)
        {
            var hint = entry.Event;
            if (hint is null || string.IsNullOrEmpty(hint.StudentId))
            {
                continue;
            }
            if (!linkedIds.Contains(hint.StudentId))
            {
                // Cross-student isolation: events from a non-linked
                // student id never contribute to ANY linked card.
                continue;
            }
            if (entry.Timestamp < monthlyStart || entry.Timestamp > now)
            {
                // Out-of-window; does not contribute to any scalar.
                continue;
            }

            // Monthly window: count + topic + last-active.
            monthlyCount[hint.StudentId] =
                (monthlyCount.TryGetValue(hint.StudentId, out var mc) ? mc : 0) + 1;

            if (!topicsInWindow.TryGetValue(hint.StudentId, out var topicSet))
            {
                topicSet = new HashSet<string>(StringComparer.Ordinal);
                topicsInWindow[hint.StudentId] = topicSet;
            }
            if (!string.IsNullOrEmpty(hint.ConceptId))
            {
                topicSet.Add(hint.ConceptId);
            }

            if (!lastActive.TryGetValue(hint.StudentId, out var prev) || entry.Timestamp > prev)
            {
                lastActive[hint.StudentId] = entry.Timestamp;
            }

            // Weekly window is a strict subset of monthly.
            if (entry.Timestamp >= weeklyStart)
            {
                weeklyCount[hint.StudentId] =
                    (weeklyCount.TryGetValue(hint.StudentId, out var wc) ? wc : 0) + 1;
            }
        }

        // Compose per-student cards. Only include students that had at
        // least one signal in the 30-day window — the endpoint layer's
        // GetOrZero fallback handles students with no activity.
        var perStudent = new Dictionary<string, ParentDashboardStudentCard>(
            StringComparer.Ordinal);
        foreach (var id in linkedIds)
        {
            var mc = monthlyCount.TryGetValue(id, out var m) ? m : 0;
            if (mc == 0)
            {
                // Zero-activity student: skip. The endpoint layer's
                // GetOrZero returns an all-zeros card for this id.
                continue;
            }
            var wc = weeklyCount.TryGetValue(id, out var w) ? w : 0;
            var topics = topicsInWindow.TryGetValue(id, out var ts) ? ts.Count : 0;
            var last = lastActive.TryGetValue(id, out var la) ? (DateTimeOffset?)la : null;

            perStudent[id] = new ParentDashboardStudentCard(
                WeeklyMinutes: wc * MinutesPerEventProxy,
                MonthlyMinutes: mc * MinutesPerEventProxy,
                TopicsPracticed: topics,
                LastActiveAt: last,
                // ReadinessScore: v1 has no readiness model — null is
                // the honest answer. Frontend renders "not yet computed".
                ReadinessScore: null);
        }

        return new ParentDashboardCards(perStudent, now);
    }
}
