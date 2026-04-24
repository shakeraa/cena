// =============================================================================
// Cena Platform — ConsentAggregate (prr-155, EPIC-PRR-A)
//
// ConsentReviewOutcome: the outcome of a parent-led review cycle
// (ConsentReviewedByParent_V1).
//
// ADR-0041 §"Age-band authorisation matrix" requires parents of Teen13to15
// and Under13 students to review and sign off on consent grants for durable
// data purposes. The review event carries an outcome enum; the aggregate
// state folds the outcome back into per-purpose grant status.
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Outcome of a parent review cycle on a set of consent purposes.
/// </summary>
public enum ConsentReviewOutcome
{
    /// <summary>Parent approved every purpose in the review set.</summary>
    Approved,

    /// <summary>Parent rejected every purpose in the review set.</summary>
    Rejected,

    /// <summary>Parent approved some purposes and rejected others (see event payload).</summary>
    Partial,

    /// <summary>Parent deferred the decision; no state change applied yet.</summary>
    Deferred
}
