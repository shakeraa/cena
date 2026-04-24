// =============================================================================
// Cena Platform — Parametric Renderer Contract (prr-200, ADR-0002)
//
// Turns a (template, slot snapshot) pair into a rendered variant: the textual
// stem, the canonical answer, and the ordered MCQ distractors. Every answer
// and every distractor routes through the CAS oracle.
//
// IMPLEMENTATIONS MUST NOT IMPORT ANY LLM CLIENT. This is enforced by the
// architecture test NoLlmInParametricPipelineTest. The SymPyParametricRenderer
// is the only production implementation; tests can substitute a pure in-memory
// fake (FakeParametricRenderer) that obeys the same "CAS-or-reject" contract.
// =============================================================================

namespace Cena.Actors.QuestionBank.Templates;

/// <summary>
/// A rendered variant ready for CAS-gated ingestion. The
/// <see cref="ParametricCompiler"/> wraps this in a <see cref="QuestionDocument"/>
/// at the outer ingestion seam and persists the (template, seed, slot-snapshot)
/// event so the rendered text can be regenerated deterministically.
/// </summary>
public sealed record ParametricVariant(
    string TemplateId,
    int TemplateVersion,
    long Seed,
    IReadOnlyList<ParametricSlotValue> SlotValues,
    string RenderedStem,
    string CanonicalAnswer,
    IReadOnlyList<ParametricDistractor> Distractors);

public sealed record ParametricDistractor(
    string MisconceptionId,
    string Text,
    string? Rationale);

/// <summary>
/// Rendered output before CAS canonicalisation. Used internally by
/// <see cref="SymPyParametricRenderer"/> to feed the CAS router.
/// </summary>
public sealed record RenderedCandidate(
    string RenderedStem,
    string SubstitutedSolutionExpr,
    IReadOnlyList<RenderedDistractorCandidate> DistractorCandidates);

public sealed record RenderedDistractorCandidate(
    string MisconceptionId,
    string SubstitutedFormulaExpr,
    string? LabelHint);

/// <summary>
/// Canonicalisation result per slot combo.
/// </summary>
public enum RendererVerdict
{
    Accepted,
    RejectedZeroDivisor,
    RejectedDisallowedShape,
    RejectedCasContradicted,
    RejectedCasUnavailable,
    RejectedNonFinite,
    RejectedRenderError
}

public sealed record RendererResult(
    RendererVerdict Verdict,
    ParametricVariant? Variant,
    string? FailureDetail,
    double LatencyMs);

/// <summary>
/// Contract implemented by SymPyParametricRenderer (production) and by test
/// fakes. A single call:
///   1. Substitutes the slot values into the stem and solution expression.
///   2. Calls the CAS router to canonicalise the solution.
///   3. Checks the canonicalised shape against <see cref="ParametricTemplate.AcceptShapes"/>.
///   4. Substitutes + canonicalises every distractor rule.
///   5. Returns a RendererResult with either an accepted Variant or a
///      structured rejection.
/// </summary>
public interface IParametricRenderer
{
    Task<RendererResult> RenderAsync(
        ParametricTemplate template,
        long seed,
        IReadOnlyList<ParametricSlotValue> slotValues,
        CancellationToken ct = default);
}
