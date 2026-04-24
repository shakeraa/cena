// =============================================================================
// Cena Platform — Parent Digest: weekly aggregator (RDY-067 F5a Phase 1).
//
// Pure function: given a parent, a week window, the set of linked minors,
// and a stream of source events, produce a DigestEnvelope with one row
// per linked minor. Deterministic, side-effect-free, fully testable.
//
// COPPA / ADR-0003 rules enforced here:
//   - Events outside the week window are dropped before aggregation.
//   - Minors with zero events in the window still get a row — that row
//     has TookABreak=true and all counters at zero, so the renderer can
//     emit the compassionate-framing line ("took a break this week —
//     that's fine") instead of dropping the minor from the digest.
//   - No free-text payloads are carried through. The event types in
//     ParentDigestPayload.cs carry only aggregate-safe fields.
//
// Mastery-gain math:
//   - Per topic within the window, find the first and last mastery
//     observation ordered by OccurredAt.
//   - Per-topic delta = last - first. Single-observation topics contribute
//     a zero delta (no growth evidence yet).
//   - Row MasteryGain = unweighted mean of per-topic deltas across the
//     topics observed in the window. Zero topics observed ⇒ zero gain.
//
// This mean-of-deltas formulation is honest under Goodhart's Law: a student
// who practices the same topic all week can raise that single delta, but
// the mean across topics only moves noticeably when breadth of growth is
// real. See docs/engineering/feature-success-metrics.md Goodhart warning
// for mastery_gain.
// =============================================================================

namespace Cena.Actors.ParentDigest;

public static class ParentDigestAggregator
{
    /// <summary>
    /// Build a weekly digest envelope for the given parent, window, and minors.
    /// </summary>
    /// <param name="parentFirstName">
    /// The parent's own first name — allowed by STEP 4b of the Phase-1 spec.
    /// Student names must NEVER be passed in; minor rows are keyed by
    /// <see cref="MinorContext.MinorLabel"/>.
    /// </param>
    /// <param name="parentLocale">Parent's resolved locale.</param>
    /// <param name="weekStart">Tenant-local Monday 00:00.</param>
    /// <param name="weekEnd">Tenant-local Sunday 23:59:59.999.</param>
    /// <param name="minors">Minors linked to this parent. One row per entry.</param>
    /// <param name="events">All source events. Will be filtered to the window.</param>
    public static DigestEnvelope BuildEnvelope(
        string parentFirstName,
        DigestLocale parentLocale,
        DateTimeOffset weekStart,
        DateTimeOffset weekEnd,
        IReadOnlyList<MinorContext> minors,
        IEnumerable<IDigestSourceEvent> events)
    {
        ArgumentNullException.ThrowIfNull(parentFirstName);
        ArgumentNullException.ThrowIfNull(minors);
        ArgumentNullException.ThrowIfNull(events);

        if (weekEnd <= weekStart)
            throw new ArgumentException(
                "weekEnd must be strictly after weekStart",
                nameof(weekEnd));

        // Materialise once. We fan out across rows, so avoid re-enumerating
        // a streaming source.
        var eventList = events as IReadOnlyList<IDigestSourceEvent> ?? events.ToList();

        // Pre-filter to the window [weekStart, weekEnd] inclusive-exclusive
        // on the right edge. A session completing exactly at Sunday 23:59:59
        // on the tenant clock belongs to THIS week; next Monday 00:00 belongs
        // to the NEXT week.
        var windowEvents = eventList
            .Where(e => e.OccurredAt >= weekStart && e.OccurredAt < weekEnd)
            .ToList();

        var rows = new List<DigestRow>(minors.Count);

        foreach (var minor in minors)
        {
            rows.Add(AggregateRow(minor, windowEvents));
        }

        return new DigestEnvelope(
            ParentFirstName: parentFirstName,
            ParentLocale: parentLocale,
            WeekStart: weekStart,
            WeekEnd: weekEnd,
            Rows: rows);
    }

    private static DigestRow AggregateRow(
        MinorContext minor,
        IReadOnlyList<IDigestSourceEvent> windowEvents)
    {
        var minorEvents = windowEvents.Where(e => e.MinorId == minor.MinorId).ToList();

        // Hours studied: sum of StudyMinutes events, converted to hours.
        var minutesStudied = minorEvents
            .OfType<DigestStudyMinutesEvent>()
            .Sum(e => e.Minutes);
        var hoursStudied = Math.Round(minutesStudied / 60.0, 2);

        // Topics covered: distinct topic IDs from TopicTouched events, sorted
        // for deterministic output (rendering and test golden assertions).
        var topicsCovered = minorEvents
            .OfType<DigestTopicTouchedEvent>()
            .Select(e => e.TopicId)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        // Session count.
        var sessionCount = minorEvents.OfType<DigestSessionCompletedEvent>().Count();

        // Mastery gain: mean of per-topic (last - first) deltas.
        var masteryEvents = minorEvents.OfType<DigestMasteryObservedEvent>().ToList();
        double masteryGain;
        if (masteryEvents.Count == 0)
        {
            masteryGain = 0.0;
        }
        else
        {
            var perTopicDeltas = masteryEvents
                .GroupBy(e => e.TopicId, StringComparer.Ordinal)
                .Select(group =>
                {
                    var ordered = group.OrderBy(e => e.OccurredAt).ToList();
                    var first = ordered[0].Mastery;
                    var last = ordered[^1].Mastery;
                    return last - first;
                })
                .ToList();

            masteryGain = perTopicDeltas.Count == 0
                ? 0.0
                : Math.Round(perTopicDeltas.Average(), 3);
        }

        var tookABreak = minorEvents.Count == 0 || hoursStudied == 0.0;

        return new DigestRow(
            MinorLabel: minor.MinorLabel,
            HoursStudied: hoursStudied,
            TopicsCovered: topicsCovered,
            MasteryGain: masteryGain,
            SessionCount: sessionCount,
            TookABreak: tookABreak);
    }
}
