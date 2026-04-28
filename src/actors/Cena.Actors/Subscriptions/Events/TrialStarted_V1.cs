// =============================================================================
// Cena Platform — TrialStarted_V1 (EPIC-PRR-I, trial-then-paywall §11.6)
//
// Emitted when a parent (or self-pay teen) starts a trial. First trial
// event in the stream `subscription-{parentSubjectId}`. Caps-snapshot is
// pinned from TrialAllotmentConfig at start time so an in-flight trial is
// immune to mid-trial config changes (per TrialAllotmentConfig.cs header
// "whichever-first semantics").
//
// All PII fields encrypted per ADR-0038. The fingerprint hash is a
// SHA-256 digest of the Stripe `card.fingerprint` returned by the
// SetupIntent — never the raw fingerprint or the card itself
// (design §5.7 single-layer-defense rule). InstituteCode trials carry an
// empty fingerprint hash (no card collected on that path).
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Caps pinned onto a trial at <c>StartTrial</c> time. Mirrors the four
/// knobs on <see cref="TrialAllotmentConfig"/>; cap-hit logic at request
/// time consults THIS snapshot, not the live config (so a config change
/// mid-trial cannot retroactively shorten/lengthen an active trial).
/// </summary>
/// <param name="TrialDurationDays">Calendar bound. 0 = no calendar bound.</param>
/// <param name="TrialTutorTurns">Total tutor turns allowed. 0 = no per-trial cap.</param>
/// <param name="TrialPhotoDiagnostics">Total diagnostic uploads allowed. 0 = no cap.</param>
/// <param name="TrialPracticeSessions">Total practice sessions allowed. 0 = no cap.</param>
public sealed record TrialCapsSnapshot(
    int TrialDurationDays,
    int TrialTutorTurns,
    int TrialPhotoDiagnostics,
    int TrialPracticeSessions)
{
    /// <summary>
    /// True iff at least one of the four knobs is non-zero. When false,
    /// the trial offering is effectively empty — <see cref="SubscriptionCommands.StartTrial"/>
    /// rejects this with <c>trial_not_offered</c>.
    /// </summary>
    public bool HasAnyAllotment =>
        TrialDurationDays > 0
        || TrialTutorTurns > 0
        || TrialPhotoDiagnostics > 0
        || TrialPracticeSessions > 0;
}

/// <summary>
/// Emitted at the start of a trial. Single first-event in the stream's
/// trial sub-cycle; followed eventually by <see cref="TrialConverted_V1"/>
/// or <see cref="TrialExpired_V1"/>.
/// </summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent subject id (ADR-0038).</param>
/// <param name="PrimaryStudentSubjectIdEncrypted">Encrypted primary student id (ADR-0038).</param>
/// <param name="TrialKind">Origin of the trial — self-pay, parent-pay, or institute-code.</param>
/// <param name="TrialStartedAt">Wall-clock start instant (UTC).</param>
/// <param name="TrialEndsAt">
/// Wall-clock end instant. Equals <c>TrialStartedAt</c> when the
/// duration knob is 0 (cap-hit-only trial); the daemon never expires
/// such a trial on calendar grounds.
/// </param>
/// <param name="FingerprintHash">
/// SHA-256 digest of the Stripe card.fingerprint. Empty for InstituteCode
/// trials (no card collected). Used by the single-layer abuse-defense
/// ledger to block recycled-trial attempts.
/// </param>
/// <param name="ExperimentVariantId">
/// Locked at trial-start per design §5.21. Empty/<c>v1-baseline</c>
/// until PRR-332 ships pricing experiments.
/// </param>
/// <param name="CapsSnapshot">
/// Trial allotment values pinned from <see cref="TrialAllotmentConfig"/>
/// at start time. Drives cap-hit decisions for the duration of the trial.
/// </param>
public sealed record TrialStarted_V1(
    string ParentSubjectIdEncrypted,
    string PrimaryStudentSubjectIdEncrypted,
    TrialKind TrialKind,
    DateTimeOffset TrialStartedAt,
    DateTimeOffset TrialEndsAt,
    string FingerprintHash,
    string ExperimentVariantId,
    TrialCapsSnapshot CapsSnapshot);
