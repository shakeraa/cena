// =============================================================================
// Cena Platform — TrialAllotmentConfigChanged_V1 (task t_b89826b8bd60)
//
// Audit-trail event appended to a dedicated `trial-allotment-config` event
// stream every time a super-admin updates the trial-allotment knobs. The
// SubscriptionAggregate is intentionally NOT touched — this is a platform-
// configuration concern, not a per-parent commercial event.
//
// Per ADR-0038 wire-encryption convention, the admin-subject-id field
// carries the encrypted form (the admin's identity is PII and must shred
// on RTBF). The numeric knob values are not PII and travel cleartext.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Audit-trail event for a super-admin update to <see cref="TrialAllotmentConfig"/>.
/// Carries the full new snapshot so the audit reader does not need to walk
/// prior events to reconstruct state.
/// </summary>
/// <param name="ChangedAt">UTC timestamp of the change.</param>
/// <param name="ChangedByAdminEncrypted">Encrypted subject id of the super-admin.</param>
/// <param name="TrialDurationDays">New value of the duration knob (0..30).</param>
/// <param name="TrialTutorTurns">New value of the tutor-turns knob (0..200).</param>
/// <param name="TrialPhotoDiagnostics">New value of the photo-diagnostics knob (0..50).</param>
/// <param name="TrialPracticeSessions">New value of the sessions knob (0..20).</param>
/// <param name="PreviousTrialEnabled">
/// Whether the prior config had any non-zero knob. Lets the audit reader
/// flag transitions from "trial offered" → "trial not offered" and vice
/// versa without walking history.
/// </param>
public sealed record TrialAllotmentConfigChanged_V1(
    DateTimeOffset ChangedAt,
    string ChangedByAdminEncrypted,
    int TrialDurationDays,
    int TrialTutorTurns,
    int TrialPhotoDiagnostics,
    int TrialPracticeSessions,
    bool PreviousTrialEnabled);
