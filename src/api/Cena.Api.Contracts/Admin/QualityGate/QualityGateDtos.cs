// =============================================================================
// Cena Platform -- Quality Gate DTOs
// Automated quality scoring for AI-generated educational questions
// =============================================================================

namespace Cena.Api.Contracts.Admin.QualityGate;

public enum GateDecision
{
    AutoApproved,
    NeedsReview,
    AutoRejected
}

public enum ViolationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Extended quality scores with per-dimension breakdown and gate decision.
/// </summary>
public sealed record QualityGateResult(
    string QuestionId,
    DimensionScores Scores,
    float CompositeScore,
    GateDecision Decision,
    IReadOnlyList<QualityViolation> Violations,
    DateTimeOffset EvaluatedAt);

public sealed record DimensionScores(
    int FactualAccuracy,
    int LanguageQuality,
    int PedagogicalQuality,
    int DistractorQuality,
    int StemClarity,
    int BloomAlignment,
    int StructuralValidity,
    int CulturalSensitivity);

public sealed record QualityViolation(
    string Dimension,
    string RuleId,
    string Description,
    ViolationSeverity Severity);

/// <summary>
/// Input to the quality gate — a question candidate to evaluate.
/// </summary>
public sealed record QualityGateInput(
    string QuestionId,
    string Stem,
    IReadOnlyList<QualityGateOption> Options,
    int CorrectOptionIndex,
    string Subject,
    string Language,       // "he", "ar", "en"
    int ClaimedBloomLevel, // 1-6
    float ClaimedDifficulty,
    string? Grade,         // "3 Units", "4 Units", "5 Units"
    IReadOnlyList<string>? ConceptIds,
    IReadOnlyList<string>? Prerequisites = null,
    IReadOnlyList<string>? AvailableLanguages = null);

public sealed record QualityGateOption(
    string Label,
    string Text,
    bool IsCorrect,
    string? DistractorRationale);

/// <summary>
/// Per-dimension thresholds configuration.
/// </summary>
public sealed record QualityGateThresholds(
    int FactualAccuracyReject,
    int FactualAccuracyApprove,
    int LanguageQualityReject,
    int LanguageQualityApprove,
    int PedagogicalQualityReject,
    int PedagogicalQualityApprove,
    int DistractorQualityReject,
    int DistractorQualityApprove,
    int StemClarityReject,
    int StemClarityApprove,
    int BloomAlignmentReject,
    int BloomAlignmentApprove,
    int StructuralValidityReject,
    int StructuralValidityApprove,
    int CulturalSensitivityHardGate,
    int CompositeReject,
    int CompositeApprove)
{
    /// <summary>Research-backed default thresholds from quality gate research report.</summary>
    public static QualityGateThresholds Default => new(
        FactualAccuracyReject: 70,    FactualAccuracyApprove: 90,
        LanguageQualityReject: 65,    LanguageQualityApprove: 85,
        PedagogicalQualityReject: 60, PedagogicalQualityApprove: 80,
        DistractorQualityReject: 45,  DistractorQualityApprove: 75,
        StemClarityReject: 60,        StemClarityApprove: 80,
        BloomAlignmentReject: 30,     BloomAlignmentApprove: 75,
        StructuralValidityReject: 60, StructuralValidityApprove: 85,
        CulturalSensitivityHardGate: 50,
        CompositeReject: 55,          CompositeApprove: 85);
}
