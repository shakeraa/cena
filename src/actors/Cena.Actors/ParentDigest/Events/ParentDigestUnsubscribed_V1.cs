// =============================================================================
// Cena Platform — ParentDigestUnsubscribed_V1 (prr-051 / EPIC-PRR-C).
//
// Appended when a parent activates the one-click unsubscribe-all link from
// an email or SMS digest. Distinct from ParentDigestPreferencesUpdated_V1
// because the audit trail must let a compliance auditor answer
// "was this a bulk unsubscribe-via-link or a granular edit?" without
// inferring from a sequence of per-purpose rows.
//
// Effect on the preferences aggregate: every known purpose is set to
// OptedOut, including SafetyAlerts (the link is the one documented
// revocation path for the default-on purpose).
//
// Token material is NOT in the event. The token nonce + signed payload
// stay in a separate nonce store for single-use enforcement; embedding
// them here would let an operator with event-store read access replay
// valid tokens.
// =============================================================================

namespace Cena.Actors.ParentDigest.Events;

/// <summary>
/// Parent used the one-click unsubscribe-all link. The aggregate is bulk
/// opted out across every known purpose after this event is applied.
/// </summary>
/// <param name="ParentActorId">Opaque parent anon id.</param>
/// <param name="StudentSubjectId">Opaque student anon id.</param>
/// <param name="InstituteId">Tenant id (ADR-0001).</param>
/// <param name="UnsubscribedAtUtc">Wall clock of the link activation.</param>
/// <param name="TokenFingerprint">
/// Short fingerprint of the token used — first 8 hex chars of the HMAC
/// signature. Lets the audit log tie the event to a specific emitted link
/// without persisting the token itself (so replay against the nonce store
/// is impossible from event-store data alone).
/// </param>
public sealed record ParentDigestUnsubscribed_V1(
    string ParentActorId,
    string StudentSubjectId,
    string InstituteId,
    DateTimeOffset UnsubscribedAtUtc,
    string TokenFingerprint);
