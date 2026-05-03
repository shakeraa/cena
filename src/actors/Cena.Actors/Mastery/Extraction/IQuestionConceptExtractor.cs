// =============================================================================
// Cena Platform — Question Concept Extractor (ADR-0062 Phase 1)
//
// The single seam through which a question's prompt + LaTeX flow on
// their way to a canonical concept set. Returns the list of
// `QuestionConcept` rows (primary first) that the caller persists via
// `QuestionConceptsExtracted_V1`. The projection mirrors the set onto
// `QuestionReadModel.Concepts` and the aggregate rebuilds the same set
// into `QuestionState.ConceptIds` on replay (per ADR-0062 §4).
//
// Tier discipline (per ADR-0062 §1):
//   * RulesOnlyConceptExtractor (this turn): wraps the existing keyword
//     classifier in BagrutDraftPersistence.ClassifyTaxonomy, runs the
//     output through BagrutTaxonomyCatalog so unknown taxonomy nodes
//     are rejected (closed-set discipline). Always returns at most one
//     concept (Primary).
//   * HybridConceptExtractor (next session): rules first; if rules
//     return 0 hits, fall through to an LLM call (Anthropic Haiku per
//     ADR-0026) that emits a structured `concepts: [{skill, role,
//     rationale}]` field. LLM output is canonicalized; unmappable
//     suggestions are rejected, never silently accepted.
//
// What this seam does NOT do:
//   * Persist anything. The caller decides whether to append the event
//     and update the document.
//   * Decide the publish gate. Calibration corpus rules + ≥10
//     items/leaf precondition live in Phase 2 (separate service).
//   * Mutate the catalog. SkillCode → leaf canonicalization is
//     read-only; any "new" skill must come through a planned taxonomy
//     update event, not by extractor side-effect.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Mastery.Extraction;

public interface IQuestionConceptExtractor
{
    /// <summary>
    /// Extract the canonical concept set for a question. Returns an
    /// empty <see cref="ExtractionResult.Concepts"/> when nothing
    /// matches — caller falls back to "unlinked" rather than inventing
    /// a SkillCode.
    /// </summary>
    Task<ExtractionResult> ExtractAsync(
        ExtractionInput input,
        CancellationToken ct = default);
}

/// <summary>
/// Input shape: the bare facts about the question.
///
/// <see cref="TrackHint"/> is optional but improves disambiguation when
/// the same skill exists at multiple Bagrut tracks (3u/4u/5u).
///
/// <see cref="RuleTierHint"/> + <see cref="RuleTierConfidence"/> let
/// callers feed the result of an existing keyword classifier (e.g.
/// `BagrutDraftPersistence.ClassifyTaxonomy`) directly to the
/// extractor without duplicating Hebrew/Arabic regex tables across
/// projects. The extractor canonicalizes the hint through
/// <see cref="BagrutTaxonomyCatalog"/> and rejects unmappable values.
/// </summary>
public sealed record ExtractionInput(
    string QuestionId,
    string? Prompt,
    string? Latex,
    string? TrackHint = null,
    string? RuleTierHint = null,
    double RuleTierConfidence = 0.0);

/// <summary>
/// Extractor output. <see cref="Strategy"/> is a versioned identifier
/// like <c>"rules_v1"</c> or <c>"rules_v1+llm_haiku4_5_v1"</c> that
/// future-self can use to roll back a bad extractor pass without losing
/// the audit trail.
/// </summary>
public sealed record ExtractionResult(
    IReadOnlyList<QuestionConcept> Concepts,
    string Strategy);
