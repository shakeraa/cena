// =============================================================================
// Cena Platform — ConsentAggregate event: ConsentRevoked_V1 (prr-155)
//
// Emitted when consent is revoked. Appended to the consent stream
// `consent-{subjectId}`.
//
// PII classification per ADR-0038:
//   - Encrypted fields: SubjectIdEncrypted, RevokedByActorIdEncrypted
//   - Plaintext fields: Purpose, RevokedByRole, RevokedAt, Reason
//
// Reason is a short, structural enum-like string ("withdrawn-by-parent",
// "automatic-expiry", "student-self-withdraw"). If a future reason needs
// free-form text we bump to V2 and encrypt.
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Consent revoked event. Appended to <c>consent-{subjectId}</c>.
/// </summary>
/// <param name="SubjectIdEncrypted">Wire-format encrypted subject id (PII).</param>
/// <param name="Purpose">Processing purpose being revoked.</param>
/// <param name="RevokedByRole">Actor role that performed the revoke.</param>
/// <param name="RevokedByActorIdEncrypted">Wire-format encrypted actor id (PII).</param>
/// <param name="RevokedAt">Wall-clock timestamp of the revoke.</param>
/// <param name="Reason">Short structural reason code, plaintext.</param>
public sealed record ConsentRevoked_V1(
    string SubjectIdEncrypted,
    ConsentPurpose Purpose,
    ActorRole RevokedByRole,
    string RevokedByActorIdEncrypted,
    DateTimeOffset RevokedAt,
    string Reason);
