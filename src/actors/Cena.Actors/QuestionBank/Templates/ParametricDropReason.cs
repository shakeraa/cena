// =============================================================================
// Cena Platform — Parametric Drop Reason Taxonomy (prr-200)
//
// Parallel to CasDropReason on the AI-generation path, but covers the
// specific rejections that can happen inside the deterministic pipeline:
//
//   1. Slot combo produced a zero divisor (e.g. a == 0 where a is a denominator)
//   2. Slot combo solved to a shape disallowed by accept_shapes (e.g. 7/3 when
//      the template declares accept_shapes=[integer])
//   3. CAS gate contradicted the derived answer (should be rare — indicates
//      a template authoring bug, not a slot issue)
//   4. CAS gate unavailable (circuit-open) — we drop rather than ship unverified
//   5. Slot predicate constraint rejected the combo (cross-slot validity)
//   6. Dedupe canonical-hash collided with an already-accepted variant
//   7. Answer value flagged non-finite (NaN / Infinity) post-evaluation
// =============================================================================

namespace Cena.Actors.QuestionBank.Templates;

public enum ParametricDropKind
{
    ZeroDivisor,
    DisallowedShape,
    CasContradicted,
    CasCircuitOpen,
    ConstraintRejected,
    DuplicateCanonicalForm,
    NonFiniteValue,
    RenderError
}

/// <summary>
/// One rejection record for a compile batch. The renderer + compiler append
/// these as they prune the slot-combo space; the CLI harness and telemetry
/// surface the aggregate count bucketed by <see cref="ParametricDropKind"/>.
/// </summary>
public sealed record ParametricDropReason(
    ParametricDropKind Kind,
    string TemplateId,
    long Seed,
    string? RenderedStem,
    string? AttemptedAnswer,
    string Detail,
    double LatencyMs)
{
    public override string ToString() =>
        $"[{Kind}] template={TemplateId} seed={Seed} detail={Detail}";
}
