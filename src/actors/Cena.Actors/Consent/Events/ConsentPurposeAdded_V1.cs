// =============================================================================
// Cena Platform — ConsentAggregate event: ConsentPurposeAdded_V1 (prr-155)
//
// Emitted when a new purpose is appended to an existing consent set
// (e.g. the platform introduces a new ConsentPurpose enum value and the
// subject opts in after the initial grant).
//
// ConsentId is the stream key reference — it is the subject stream
// identifier `consent-{subjectId}`, NOT the raw subject id. The stream-
// key prefix makes the correlation non-reversible to a naked subject id
// without knowing the prefix convention, and the correlation is
// authorisation-safe to log in audit trails.
//
// PII classification per ADR-0038:
//   - ConsentId is the stream key (plaintext, matches Marten stream-key semantics)
//   - NewPurpose enum (plaintext)
//   - AddedByRole enum (plaintext)
//   - AddedAt timestamp (plaintext)
//
// Note: this event intentionally does not carry the actor id. Audit
// correlation uses the paired ConsentGranted_V1 emitted by the aggregate
// alongside it.
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Consent purpose added event. Appended to <c>consent-{subjectId}</c>.
/// </summary>
/// <param name="ConsentId">Stream key of the owning consent aggregate.</param>
/// <param name="NewPurpose">Processing purpose being added.</param>
/// <param name="AddedByRole">Actor role that added the purpose.</param>
/// <param name="AddedAt">Wall-clock timestamp.</param>
public sealed record ConsentPurposeAdded_V1(
    string ConsentId,
    ConsentPurpose NewPurpose,
    ActorRole AddedByRole,
    DateTimeOffset AddedAt);
