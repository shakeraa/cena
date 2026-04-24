// =============================================================================
// Cena Platform — ConsentAggregate event: ConsentGranted_V2 (prr-123)
//
// V2 extends V1 with the exact version string of the privacy policy the
// grantor accepted. This is the binding audit record for "the parent /
// student accepted v1.0.0 of the policy on date D before granting purpose P".
//
// Back-compat: ConsentGranted_V1 events pre-dating this change are upcast
// to V2 with PolicyVersionAccepted = "v0.0.0-pre-versioning" (the sentinel
// chosen by prr-123 so auditors can trivially grep for grants that predate
// version tracking). The upcaster is wired in EventUpcasters.
//
// PII classification (unchanged from V1 per ADR-0038):
//   - Encrypted: SubjectIdEncrypted, GrantedByActorIdEncrypted
//   - Plaintext: Purpose, Scope, GrantedByRole, GrantedAt, ExpiresAt,
//                PolicyVersionAccepted
// PolicyVersionAccepted is a structural SemVer-style string, NOT PII.
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Consent granted event, V2. Adds <paramref name="PolicyVersionAccepted"/>.
/// Appended to <c>consent-{subjectId}</c>.
/// </summary>
/// <param name="SubjectIdEncrypted">Wire-format encrypted subject id (PII).</param>
/// <param name="Purpose">Processing purpose being granted.</param>
/// <param name="Scope">Structural scope label, plaintext.</param>
/// <param name="GrantedByRole">Actor role that performed the grant.</param>
/// <param name="GrantedByActorIdEncrypted">Wire-format encrypted actor id (PII).</param>
/// <param name="GrantedAt">Wall-clock timestamp of the grant.</param>
/// <param name="ExpiresAt">Optional grant expiry; null = indefinite.</param>
/// <param name="PolicyVersionAccepted">
/// Exact version string (e.g. <c>"v1.0.0 2026-04-21"</c>) of the privacy
/// policy the grantor accepted. Upcast V1 events carry
/// <c>"v0.0.0-pre-versioning"</c> so the audit surface always has a value.
/// </param>
public sealed record ConsentGranted_V2(
    string SubjectIdEncrypted,
    ConsentPurpose Purpose,
    string Scope,
    ActorRole GrantedByRole,
    string GrantedByActorIdEncrypted,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string PolicyVersionAccepted);
