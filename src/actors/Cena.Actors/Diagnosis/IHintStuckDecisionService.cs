// =============================================================================
// Cena Platform — IHintStuckDecisionService (RDY-063 Phase 2b)
//
// Single entry point for the hint endpoint. Combines:
//   - classifier invocation (bounded by HintAdjustmentTimeoutMs when
//     adjustment is on; fire-and-forget otherwise)
//   - hint-level adjustment via IHintLevelAdjuster
//   - persistence via IStuckDiagnosisRepository (always background)
//   - structured [STUCK_DIAG] log line
//
// Replaces IHintStuckShadowService as the endpoint-facing surface.
// The shadow service is kept in DI for reuse in non-hint paths (e.g.,
// future live-help routing) but the /hint endpoint calls this service.
// =============================================================================

using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Result surfaced back to the hint endpoint. Always returns a valid
/// AdjustedLevel — when the classifier is off, timed out, or errored,
/// AdjustedLevel equals the requested level and Adjusted == false.
/// </summary>
public sealed record HintDecisionOutcome(
    int AdjustedLevel,
    bool Adjusted,
    string ReasonCode,
    StuckType? Primary,
    float? PrimaryConfidence,
    int LatencyMs);

public interface IHintStuckDecisionService
{
    /// <summary>
    /// Decide the effective hint level given the classifier output.
    /// NEVER throws. Guarantees a bounded latency per
    /// <see cref="StuckClassifierOptions.HintAdjustmentTimeoutMs"/>.
    /// </summary>
    Task<HintDecisionOutcome> DecideAsync(
        string studentId,
        string sessionId,
        string questionId,
        LearningSessionQueueProjection queue,
        QuestionDocument question,
        int requestedLevel,
        int maxLevel,
        string locale,
        CancellationToken ct = default);
}
