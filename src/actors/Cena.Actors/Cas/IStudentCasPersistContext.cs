// =============================================================================
// Cena Platform — Student-side persist context for CAS-gated variant generation
//
// PRR-252 — when a student requests a variant via PRR-245's variant-generation
// endpoint, the endpoint constructs an IStudentCasPersistContext capturing
// the lineage fields the persister needs to:
//
//   1. Stamp the new QuestionState stream with ADR-0059 §6 lineage
//      (sourceShailonCode, questionIndex, variationKind) so audit can
//      reconstruct "this variant was generated from שאלון X question Y".
//
//   2. Carry the source's provenance (typically MinistryBagrut for the
//      reference library path) so downstream IItemDeliveryGate enforcement
//      remains correct — the variant the persister WRITES is a recreation
//      (deliverable), but its source is reference-only (non-deliverable).
//
//   3. Derive an idempotency key for cost-amortizing dedup: two students
//      requesting the same {sourceShailonCode, questionIndex, variationKind,
//      parametricSeed?} return the same persisted variant document instead
//      of regenerating + re-grading.
//
// Endpoint-layer contract (PRR-252 §3): the variant endpoint MUST call
// ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId) BEFORE
// invoking ICasGatedQuestionPersister.PersistAsync. The persister itself is
// identity-agnostic — that boundary is the same as the admin-side flow.
// =============================================================================

using Cena.Actors.Content;

namespace Cena.Actors.Cas;

/// <summary>
/// Variation strategy applied to the source. Drives prompt template selection
/// and idempotency-key composition (per ADR-0059 §5).
/// </summary>
public enum VariationKind
{
    /// <summary>Numeric / coefficient swap on the same problem template
    /// (cheap, deterministic given parametricSeed).</summary>
    Parametric = 1,

    /// <summary>Restructured problem with new hooks but the same skill
    /// taxonomy (LLM-generated, requires CAS verify before persist).</summary>
    Structural = 2,
}

/// <summary>
/// Lightweight value-type context the variant-generation endpoint passes
/// alongside <see cref="GatedPersistContext"/> when calling
/// <see cref="ICasGatedQuestionPersister"/> from the student API.
///
/// The persister uses <see cref="IdempotencyKey"/> (when non-null) to dedup
/// repeated generations of the same source/seed pair across students.
/// </summary>
public sealed record StudentCasPersistContext(
    /// <summary>Calling student id (already verified by the endpoint
    /// auth gate; used here only for audit-log lineage, NOT identity
    /// re-checks).</summary>
    string StudentId,

    /// <summary>Provenance of the SOURCE item the variant was generated
    /// from. Typically <see cref="ProvenanceKind.MinistryBagrut"/> for the
    /// reference-library flow; null for purely synthetic generations.</summary>
    Provenance? SourceProvenance,

    /// <summary>ADR-0059 §6 lineage — Ministry שאלון code (e.g. "035582")
    /// the source belongs to. Null when the variant has no Ministry-Bagrut
    /// ancestor.</summary>
    string? SourceShailonCode,

    /// <summary>ADR-0059 §6 lineage — 1-based question index within the
    /// source שאלון. Null when SourceShailonCode is null.</summary>
    int? SourceQuestionIndex,

    /// <summary>How the variant was derived. Drives prompt template
    /// selection (parametric reuses the source structure; structural
    /// rebuilds it).</summary>
    VariationKind VariationKind,

    /// <summary>Deterministic seed for parametric variants. Mixed into
    /// <see cref="IdempotencyKey"/> so repeated requests with the same
    /// seed dedup; structural variants use random seeds and bypass dedup
    /// (every structural request is a fresh LLM generation).</summary>
    int? ParametricSeed = null)
{
    /// <summary>
    /// Cost-amortizing dedup key. Two student requests with the same key
    /// resolve to the same persisted variant document (PRR-252 §4 + ADR-0059
    /// §5). Null disables dedup — the persister will always regenerate.
    /// </summary>
    public string? IdempotencyKey
    {
        get
        {
            // Structural variants are non-deterministic by design; no dedup.
            if (VariationKind == VariationKind.Structural) return null;
            // Need at least source lineage + seed to form a stable key.
            if (string.IsNullOrWhiteSpace(SourceShailonCode)) return null;
            if (SourceQuestionIndex is null) return null;
            if (ParametricSeed is null) return null;
            return $"variant|{SourceShailonCode}|{SourceQuestionIndex}|parametric|{ParametricSeed}";
        }
    }
}
