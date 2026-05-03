// =============================================================================
// Cena Platform — IHintLevelAdjuster (RDY-063 Phase 2b)
//
// Pure function: given a requested hint level, the scaffolding budget,
// and a stuck-type diagnosis, return the hint level the generator
// should actually use. Implementations must be side-effect-free and
// deterministic (same inputs → same output) so adjustment behaviour is
// reproducible from the persisted diagnosis alone.
//
// The adjuster must NEVER:
//   - produce a level < 1 or > maxHints
//   - emit text content (that's the hint generator's job)
//   - consult any cross-session / student-profile data
//
// Architectural invariant: no adjustment is *additive information*.
// The hint text the student sees is still drawn from the pre-authored
// ladder — we only change which rung we step onto.
// =============================================================================

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Result of a single adjustment decision.
/// </summary>
public sealed record HintLevelAdjustment(
    int OriginalLevel,
    int AdjustedLevel,
    bool Changed,
    string ReasonCode);

public interface IHintLevelAdjuster
{
    /// <summary>
    /// Decide the effective hint level.
    /// </summary>
    /// <param name="requestedLevel">Level the student asked for (1-based).</param>
    /// <param name="maxLevel">Scaffolding-budget max hints (from ScaffoldingMetadata).</param>
    /// <param name="diagnosis">Classifier output (may be null / Unknown).</param>
    /// <param name="minConfidence">Required primary-confidence to act.</param>
    HintLevelAdjustment Adjust(
        int requestedLevel,
        int maxLevel,
        StuckDiagnosis? diagnosis,
        float minConfidence);
}
