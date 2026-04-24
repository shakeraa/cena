// =============================================================================
// Cena Platform — IStuckClassifierLlm (RDY-063 Phase 1)
//
// Narrow interface around the LLM round-trip used by the stuck
// classifier. Separate from ITutorLlmService because:
//   - it returns a structured label, not a stream
//   - it uses a different (cheaper) model (Haiku vs Sonnet)
//   - it has a distinct cost-budget tier
//   - it's trivially mockable in tests
// =============================================================================

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Serialisable result of a single LLM classification call. The LLM
/// returns JSON matching this shape; a safe parser reads it, rejects
/// unknown values, and returns LlmClassificationResult.Invalid on
/// malformed output.
/// </summary>
public sealed record LlmClassificationResult(
    bool Success,
    StuckType Primary,
    float PrimaryConfidence,
    StuckType Secondary,
    float SecondaryConfidence,
    string? RawReason,           // never emitted to the student; classifier-internal tracing only
    double EstimatedCostUsd,
    int LatencyMs,
    string? ErrorCode            // e.g. "parse_failure", "rate_limited", "timeout"
)
{
    public static LlmClassificationResult Invalid(string errorCode, int latencyMs) =>
        new(false, StuckType.Unknown, 0, StuckType.Unknown, 0, null, 0, latencyMs, errorCode);
}

public interface IStuckClassifierLlm
{
    /// <summary>
    /// Make the classification call. Implementations MUST NOT throw
    /// on network / parse / timeout errors; return
    /// <see cref="LlmClassificationResult.Invalid"/> with an error code.
    /// </summary>
    Task<LlmClassificationResult> ClassifyAsync(StuckContext ctx, CancellationToken ct = default);
}
