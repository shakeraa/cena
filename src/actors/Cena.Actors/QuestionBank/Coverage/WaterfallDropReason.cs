// =============================================================================
// Cena Platform — Waterfall Drop Reason (prr-201)
//
// Taxonomy of why a candidate variant was dropped during the waterfall.
// Every drop is a counted-labelled Prometheus metric so the admin heatmap
// (prr-209) can distinguish "LLM cost-capped" from "Ministry-similarity"
// from "CAS-rejected" without re-reading logs.
// =============================================================================

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// Why a single candidate was dropped. Stages 2 and 3 only — stage 1 drops
/// are accounted for by <c>ParametricDropReason</c> on the compile report.
/// </summary>
public enum WaterfallDropKind
{
    /// <summary>Stage 2: CAS gate did not verify the candidate answer.</summary>
    CasRejected,

    /// <summary>Stage 2: the Ministry-similarity score exceeded the threshold (ADR-0043).</summary>
    MinistrySimilarity,

    /// <summary>Stage 2: canonical-form hash already emitted by stage 1 or earlier stage-2 output.</summary>
    Duplicate,

    /// <summary>Stage 2: per-institute daily budget exceeded; stage 2 short-circuits to stage 3.</summary>
    BudgetCap,

    /// <summary>Stage 2: isomorph generator returned an error or no candidates.</summary>
    IsomorphGeneratorError,

    /// <summary>Stage 2: circuit breaker open on the LLM provider — stage 2 treated as unavailable.</summary>
    CircuitOpen
}

/// <summary>
/// A single candidate drop record. Carries enough detail for the curator
/// task body and the admin heatmap drill-down.
/// </summary>
public sealed record WaterfallDrop(
    WaterfallStage Stage,
    WaterfallDropKind Kind,
    string? CandidatePreview,
    string? Detail,
    double? MinistrySimilarityScore = null);
