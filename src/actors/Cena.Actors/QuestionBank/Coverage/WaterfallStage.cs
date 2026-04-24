// =============================================================================
// Cena Platform — Waterfall stage enum (prr-201)
// =============================================================================

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// Waterfall stage identity. The orchestrator cascades strictly in the order
/// Parametric → LlmIsomorph → CuratorQueue; each stage runs only if the prior
/// stage failed to fill the target.
/// </summary>
public enum WaterfallStage
{
    /// <summary>Strategy 1 — deterministic parametric compile (prr-200).</summary>
    Parametric,

    /// <summary>Strategy 2 — LLM isomorph of the stage-1 seeds (tier3).</summary>
    LlmIsomorph,

    /// <summary>Strategy 3 — enqueue a human-curator task.</summary>
    CuratorQueue
}
