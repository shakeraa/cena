// =============================================================================
// Cena Platform — ConsentAggregate event: StudentVisibilityVetoed_V1 (prr-052)
//
// Emitted when a Teen16to17 or Adult student opts their parent OUT of a
// specific non-safety purpose on the parent dashboard. Distinct from
// ConsentRevoked_V1 (which revokes a consent that the subject or their
// parent had previously granted) — a veto narrows PARENT VISIBILITY of
// a purpose that may still be granted for other processors (e.g. the
// student still wants misconception detection for their own tutor UI
// but doesn't want the parent to see the summary).
//
// Appended to `consent-{studentSubjectId}`.
//
// Legal basis:
//   ADR-0041 §"Student can withdraw consent" row for Teen16to17:
//   "Yes, for all purposes except legally-required reporting".
//   AgeBandPolicy.CanStudentVetoPurpose enforces that only Teen16to17+
//   bands can produce this event.
//
// PII classification per ADR-0038:
//   - Encrypted: StudentSubjectIdEncrypted, InitiatorActorIdEncrypted
//   - Plaintext: Purpose, Initiator (enum), InstituteId, VetoedAt, Reason
//
// Initiator semantics:
//   VetoInitiator.Student       — the 16+ student self-vetoed
//   VetoInitiator.InstitutePolicy — the institute-level override
//                                    narrowed visibility automatically
//                                    (ADR-0041 "institute may be stricter"
//                                    carve-out; audited so operators can
//                                    see who narrowed the view)
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Who initiated the visibility veto — the student themselves, or an
/// institute policy override.
/// </summary>
public enum VetoInitiator
{
    /// <summary>The student (Teen16to17 or Adult) self-vetoed.</summary>
    Student,

    /// <summary>An institute-policy rule narrowed visibility automatically.</summary>
    InstitutePolicy,
}

/// <summary>
/// Student-visibility veto event. Appended to <c>consent-{studentSubjectId}</c>.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">Wire-format encrypted student id.</param>
/// <param name="Purpose">The purpose being hidden from the parent.</param>
/// <param name="Initiator">Who triggered the veto (Student or InstitutePolicy).</param>
/// <param name="InitiatorActorIdEncrypted">Wire-format encrypted initiator id.</param>
/// <param name="InstituteId">Tenant scope for the veto (plaintext).</param>
/// <param name="VetoedAt">Wall-clock timestamp.</param>
/// <param name="Reason">Short free-text justification (plaintext, no PII).</param>
public sealed record StudentVisibilityVetoed_V1(
    string StudentSubjectIdEncrypted,
    ConsentPurpose Purpose,
    VetoInitiator Initiator,
    string InitiatorActorIdEncrypted,
    string InstituteId,
    DateTimeOffset VetoedAt,
    string Reason);

/// <summary>
/// Companion event: student restores parent visibility of a previously
/// vetoed purpose. Flips the logical state back to "parent may see".
/// </summary>
/// <param name="StudentSubjectIdEncrypted">Wire-format encrypted student id.</param>
/// <param name="Purpose">The purpose being restored.</param>
/// <param name="Initiator">Who triggered the restore.</param>
/// <param name="InitiatorActorIdEncrypted">Wire-format encrypted initiator id.</param>
/// <param name="InstituteId">Tenant scope (plaintext).</param>
/// <param name="RestoredAt">Wall-clock timestamp.</param>
public sealed record StudentVisibilityRestored_V1(
    string StudentSubjectIdEncrypted,
    ConsentPurpose Purpose,
    VetoInitiator Initiator,
    string InitiatorActorIdEncrypted,
    string InstituteId,
    DateTimeOffset RestoredAt);
