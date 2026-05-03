// =============================================================================
// Cena Platform — ConsentAggregate event: ConsentReviewedByParent_V1 (prr-155)
//
// Emitted when a parent completes a review cycle on a student's consent
// set. Maps to ADR-0041's parent-review workflow for Under13 and Teen13to15
// bands.
//
// PII classification per ADR-0038:
//   - Encrypted fields: StudentSubjectIdEncrypted, ParentActorIdEncrypted
//   - Plaintext fields: PurposesReviewed (enum list), Outcome (enum),
//                       ReviewedAt (timestamp)
//
// Callers MUST encrypt both subject ids before constructing. The review
// outcome drives subsequent aggregate state transitions; folding is
// delegated to ConsentState.
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Parent-review event. Appended to <c>consent-{studentSubjectId}</c>.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">Wire-format encrypted student subject id (PII).</param>
/// <param name="ParentActorIdEncrypted">Wire-format encrypted parent actor id (PII).</param>
/// <param name="PurposesReviewed">Purposes that were part of the review cycle.</param>
/// <param name="Outcome">Aggregate outcome of the review.</param>
/// <param name="ReviewedAt">Wall-clock timestamp of the review completion.</param>
public sealed record ConsentReviewedByParent_V1(
    string StudentSubjectIdEncrypted,
    string ParentActorIdEncrypted,
    IReadOnlyList<ConsentPurpose> PurposesReviewed,
    ConsentReviewOutcome Outcome,
    DateTimeOffset ReviewedAt);
