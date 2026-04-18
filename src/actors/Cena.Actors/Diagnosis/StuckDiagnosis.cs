// =============================================================================
// Cena Platform — StuckDiagnosis (RDY-063 Phase 1)
//
// Label-only output of a stuck-type classification. No math claims, no
// LaTeX fragments, no free-text advice; consumers pick the actual copy
// from pre-authored templates keyed by (stuckType, strategy, locale).
//
// Primary + secondary (top-2) are returned always so downstream logic
// can make adaptive decisions (e.g., "if primary is Misconception with
// low confidence and secondary is Strategic, lean toward decomposition
// prompt first"). Confidence is the classifier's own certainty, not a
// model-output-probability (heuristic rules carry fixed confidence
// bands).
// =============================================================================

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Result of a single classifier invocation.
/// </summary>
public sealed record StuckDiagnosis(
    StuckType Primary,
    float PrimaryConfidence,
    StuckType Secondary,
    float SecondaryConfidence,
    StuckScaffoldStrategy SuggestedStrategy,
    string? FocusChapterId,
    bool ShouldInvolveTeacher,
    StuckDiagnosisSource Source,
    string ClassifierVersion,
    DateTimeOffset DiagnosedAt,
    int LatencyMs,
    string? SourceReasonCode           // short machine-readable tag, e.g. "heuristic.zero_attempts_long_time"
)
{
    /// <summary>
    /// A low-confidence Unknown result — returned when the classifier
    /// refuses to commit (e.g., confidence below threshold, LLM errored
    /// without heuristic fallback, or feature flag off).
    /// </summary>
    public static StuckDiagnosis Unknown(
        string classifierVersion,
        StuckDiagnosisSource source,
        int latencyMs,
        string? reasonCode = null,
        DateTimeOffset? at = null) => new(
            Primary: StuckType.Unknown,
            PrimaryConfidence: 0f,
            Secondary: StuckType.Unknown,
            SecondaryConfidence: 0f,
            SuggestedStrategy: StuckScaffoldStrategy.Unspecified,
            FocusChapterId: null,
            ShouldInvolveTeacher: false,
            Source: source,
            ClassifierVersion: classifierVersion,
            DiagnosedAt: at ?? DateTimeOffset.UtcNow,
            LatencyMs: latencyMs,
            SourceReasonCode: reasonCode);

    /// <summary>
    /// True when the caller should trust this diagnosis enough to change
    /// scaffolding. Below threshold, fall back to the existing
    /// level-based hint ladder.
    /// </summary>
    public bool IsActionable(float minConfidence)
        => Primary != StuckType.Unknown && PrimaryConfidence >= minConfidence;
}
