// =============================================================================
// Cena Platform — ParentChildBinding (prr-009, EPIC-PRR-C)
//
// Canonical domain record representing "parent P is authorised to act on
// behalf of student S at institute I". The binding is the authoritative
// source of truth for parent → child authorisation; the JWT `parent_of`
// claims are an advisory cache re-derived from this record at login and
// every session refresh (per ADR-0041).
//
// The binding is per-(studentSubjectId, instituteId) pair, NOT per student
// alone. ADR-0041 requires: "A parent who has been granted visibility for
// their child's bagrut enrolment at school A has no parent-session
// visibility for that same child's SAT enrolment at private tutor B
// unless an explicit grant has been recorded for institute B." Institute
// A and institute B may be competitors; A's grant cannot be given away
// on B's behalf.
// =============================================================================

namespace Cena.Actors.Parent;

/// <summary>
/// An active grant linking a parent actor to a student subject inside a
/// specific institute's tenant boundary.
/// </summary>
/// <param name="ParentActorId">
/// Opaque, anon parent identifier. Never a name, email, or phone.
/// </param>
/// <param name="StudentSubjectId">
/// Opaque, anon student identifier. Same value used as Marten stream
/// key for student-scoped events (<c>studentAnonId</c>).
/// </param>
/// <param name="InstituteId">
/// The institute inside which this grant is valid. ADR-0001 multi-institute
/// tenant boundary.
/// </param>
/// <param name="GrantedAtUtc">
/// When the grant was recorded. Emitted alongside the
/// <c>ParentalConsentGranted_V1</c> event on the consent stream.
/// </param>
/// <param name="RevokedAtUtc">
/// Non-null when the grant has been revoked; the record is kept in
/// history for audit but the binding no longer authorises access.
/// </param>
public sealed record ParentChildBinding(
    string ParentActorId,
    string StudentSubjectId,
    string InstituteId,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? RevokedAtUtc = null)
{
    /// <summary>True when the binding is currently active (not revoked).</summary>
    public bool IsActive => RevokedAtUtc is null;
}
