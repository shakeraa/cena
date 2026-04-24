// =============================================================================
// Cena Platform — Waterfall Result (prr-201)
//
// Aggregate result from a single FillRungAsync call. Always returned
// successfully (including the "gap" case) so the caller can inspect per-stage
// outcomes without exception handling. Stage 3 triggers when filled < target
// after stages 1+2 — the CuratorTaskId is populated when that branch runs.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// Per-stage outcome. <see cref="Executed"/> is false for a stage that the
/// cascade skipped (e.g. stage 2 was skipped because stage 1 already met
/// the target).
/// </summary>
public sealed record StageOutcome(
    WaterfallStage Stage,
    bool Executed,
    int AcceptedCount,
    IReadOnlyList<ParametricVariant> AcceptedVariants,
    IReadOnlyList<WaterfallDrop> Drops,
    double ElapsedMs);

/// <summary>
/// Aggregate result. <see cref="Filled"/> is the total accepted across
/// stages 1 and 2; stage 3 does not produce variants directly. <see cref="Gap"/>
/// is <c>max(0, Target - Filled)</c>.
/// </summary>
public sealed record WaterfallResult(
    CoverageCell Cell,
    int Target,
    int Filled,
    IReadOnlyList<StageOutcome> Stages,
    string? CuratorTaskId,
    IReadOnlyList<ParametricVariant> AllAcceptedVariants)
{
    public int Gap => Math.Max(0, Target - Filled);
    public bool IsFullyCovered => Filled >= Target;
    public bool UsedLlm => Stages.Any(s => s.Stage == WaterfallStage.LlmIsomorph && s.Executed);
    public bool UsedCurator => CuratorTaskId is not null;
}
