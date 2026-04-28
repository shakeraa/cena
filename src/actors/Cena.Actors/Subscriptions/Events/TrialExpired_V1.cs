// =============================================================================
// Cena Platform — TrialExpired_V1 (EPIC-PRR-I, trial-then-paywall §11.7)
//
// Emitted when a trial reaches its end without conversion. The
// <see cref="TrialExpiryWorker"/> (separate task) is the only emitter on
// the timer path; the resolver / paywall filter never emits this event.
//
// The utilisation block is the §5.18 telemetry payload merged into the
// domain event so analytics computes funnel metrics from a single stream
// per design §11.7.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Trial-utilisation snapshot. Carried verbatim on <see cref="TrialExpired_V1"/>
/// and on <see cref="TrialConverted_V1"/> so funnel analytics can merge
/// the two streams without re-deriving counts.
/// </summary>
/// <param name="TutorTurnsUsed">Tutor turns consumed during the trial.</param>
/// <param name="PhotoDiagnosticsUsed">Photo diagnostic uploads consumed.</param>
/// <param name="SessionsStarted">Practice sessions started.</param>
/// <param name="DaysActive">Distinct UTC days on which the student was active.</param>
/// <param name="HitCapBeforeExpiry">
/// True when at least one allotment cap was reached before the calendar
/// boundary fired. False when the trial expired on time only.
/// </param>
public sealed record TrialUtilization(
    int TutorTurnsUsed,
    int PhotoDiagnosticsUsed,
    int SessionsStarted,
    int DaysActive,
    bool HitCapBeforeExpiry);

/// <summary>
/// Trial ended without conversion. <see cref="Outcome"/> is always
/// <c>"expired"</c> on this event type; the analytics-funnel string is
/// kept inline (not enum) so future post-launch outcomes (e.g.,
/// <c>"abandoned"</c>) can be added without a V2 event.
/// </summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent subject id (ADR-0038).</param>
/// <param name="PrimaryStudentSubjectIdEncrypted">Encrypted primary student id (ADR-0038).</param>
/// <param name="TrialEndedAt">Wall-clock end instant (UTC).</param>
/// <param name="Outcome">Always <c>"expired"</c> for this V1.</param>
/// <param name="Utilization">Final utilisation snapshot at expiry time.</param>
public sealed record TrialExpired_V1(
    string ParentSubjectIdEncrypted,
    string PrimaryStudentSubjectIdEncrypted,
    DateTimeOffset TrialEndedAt,
    string Outcome,
    TrialUtilization Utilization)
{
    /// <summary>Canonical outcome string for natural expiry.</summary>
    public const string OutcomeExpired = "expired";
}
