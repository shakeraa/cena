// =============================================================================
// Cena Platform — ConsentAggregate event: ConsentGranted_V1 (prr-155)
//
// Emitted when a valid consent grant is recorded. Appended to the consent
// stream `consent-{subjectId}` owned by ConsentAggregate.
//
// PII classification per ADR-0038 §"Field classification policy":
//   - Encrypted fields: SubjectIdEncrypted, GrantedByActorIdEncrypted
//   - Plaintext fields: Purpose (enum), Scope, GrantedByRole (enum),
//                       GrantedAt, ExpiresAt
//
// Callers MUST wrap SubjectId and GrantedByActorId through
// EncryptedFieldAccessor.EncryptAsync before constructing the record.
// Readers MUST route the *Encrypted fields through TryDecryptAsync and
// handle the ErasedSentinel.Value return.
//
// The Scope string is deliberately plaintext: it is a short, structural
// category label ("classroom-A", "durable-profile") not a PII identifier.
// If a project adds a scope value that carries PII, the event must bump
// to V2 and the field gets encrypted.
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Consent granted event. Appended to <c>consent-{subjectId}</c>.
/// </summary>
/// <param name="SubjectIdEncrypted">Wire-format encrypted subject id (PII).</param>
/// <param name="Purpose">Processing purpose being granted (plaintext enum).</param>
/// <param name="Scope">Structural scope label, plaintext (e.g. institute id, device).</param>
/// <param name="GrantedByRole">Actor role that performed the grant.</param>
/// <param name="GrantedByActorIdEncrypted">Wire-format encrypted actor id (PII).</param>
/// <param name="GrantedAt">Wall-clock timestamp of the grant.</param>
/// <param name="ExpiresAt">Optional grant expiry; null = indefinite.</param>
public sealed record ConsentGranted_V1(
    string SubjectIdEncrypted,
    ConsentPurpose Purpose,
    string Scope,
    ActorRole GrantedByRole,
    string GrantedByActorIdEncrypted,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt);
