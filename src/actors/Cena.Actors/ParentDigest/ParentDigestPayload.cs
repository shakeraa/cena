// =============================================================================
// Cena Platform — Parent Digest: payload types (RDY-067 F5a Phase 1).
//
// Aggregate-only, privacy-first shape for the weekly parent digest.
// Key COPPA / ADR-0003 invariants carried by this file:
//   - DigestRow carries NO per-child names. A privacy-preserving MinorLabel
//     (e.g. "Your 11th-grader in 5-unit math") identifies the row without
//     exposing the student's name to the parent-facing template pipeline.
//   - DigestRow carries NO misconception labels, NO stuck-type codes, NO
//     buggy-rule identifiers, and NO session transcripts. Aggregates only.
//   - The parent-facing envelope may address the PARENT by first name
//     (their own consented account datum), but never the student.
//
// Source events are declared here as a small interface family so the
// Phase-1 aggregator is decoupled from concrete Marten event types. The
// Phase-2 adapter layer maps the actual session / mastery events into
// these records before feeding the aggregator.
// =============================================================================

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Parent's resolved locale for the digest. Matches the three supported
/// student-web locales (en / ar / he). The digest renders in the parent's
/// locale regardless of the minor's locale.
/// </summary>
public enum DigestLocale
{
    En,
    Ar,
    He,
}

/// <summary>
/// Minimal common shape for source events feeding the weekly aggregator.
/// Events carry MinorId for grouping and OccurredAt for window filtering.
/// Concrete Phase-2 adapters map Marten session / mastery events into
/// implementations of these records.
/// </summary>
public interface IDigestSourceEvent
{
    Guid MinorId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// One observation of study time. Minutes is the elapsed study time
/// attributable to the student (active engagement, not wall-clock).
/// </summary>
public sealed record DigestStudyMinutesEvent(
    Guid MinorId,
    DateTimeOffset OccurredAt,
    int Minutes) : IDigestSourceEvent;

/// <summary>
/// A topic the minor interacted with during the window. Topic identifier
/// is the syllabus topic code, never a free-text tutor turn.
/// </summary>
public sealed record DigestTopicTouchedEvent(
    Guid MinorId,
    DateTimeOffset OccurredAt,
    string TopicId) : IDigestSourceEvent;

/// <summary>
/// A mastery observation. <see cref="Mastery"/> is the BKT P(known) value
/// in [0, 1] at the moment of observation. Multiple observations per
/// topic in the same window are expected; the aggregator computes the
/// first→last delta per topic and averages across topics.
/// </summary>
public sealed record DigestMasteryObservedEvent(
    Guid MinorId,
    DateTimeOffset OccurredAt,
    string TopicId,
    double Mastery) : IDigestSourceEvent;

/// <summary>
/// A session the minor completed. Counted to surface pacing signal.
/// </summary>
public sealed record DigestSessionCompletedEvent(
    Guid MinorId,
    DateTimeOffset OccurredAt) : IDigestSourceEvent;

/// <summary>
/// Per-minor metadata the aggregator needs to produce a row. MinorLabel
/// is an already-computed privacy-preserving label — callers must NOT
/// pass the student's given name.
/// </summary>
public sealed record MinorContext(Guid MinorId, string MinorLabel);

/// <summary>
/// A single aggregated row in the weekly digest. Exactly one row per
/// linked minor, even when the minor had zero activity (TookABreak=true).
/// </summary>
public sealed record DigestRow(
    string MinorLabel,
    double HoursStudied,
    IReadOnlyList<string> TopicsCovered,
    double MasteryGain,
    int SessionCount,
    bool TookABreak);

/// <summary>
/// Envelope wrapping one or more rows under a single parent recipient.
/// WeekStart and WeekEnd define the tenant-local window [Mon 00:00, Sun 23:59].
/// </summary>
public sealed record DigestEnvelope(
    string ParentFirstName,
    DigestLocale ParentLocale,
    DateTimeOffset WeekStart,
    DateTimeOffset WeekEnd,
    IReadOnlyList<DigestRow> Rows);
