// =============================================================================
// Cena Platform -- L3 Explanation Request (SAI-004)
// Rich context record collecting ALL available student signals for
// personalized explanation generation.
//
// L3 fires ONLY when L2 cache misses. It adds full student context
// (mastery, affect, behavior, instructional state) to the LLM prompt
// for deeply personalized explanations.
//
// PRIVACY: No student ID, name, or PII is ever included.
// All signals are anonymous behavioral/cognitive metrics.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Services;

/// <summary>
/// Full student context for L3 personalized explanation generation.
/// Collected from ConceptMasteryState, affect detectors, and behavioral signals.
/// </summary>
public sealed record L3ExplanationRequest
{
    // ── Question context ──
    public required string QuestionId { get; init; }
    public required string QuestionStem { get; init; }
    public required string CorrectAnswer { get; init; }
    public required string StudentAnswer { get; init; }
    public required string ErrorType { get; init; }
    public required string Subject { get; init; }
    public required string Language { get; init; }
    public string? StaticExplanation { get; init; }
    public string? DistractorRationale { get; init; }

    // ── Mastery context (from ConceptMasteryState) ──
    public double MasteryProbability { get; init; }
    public double RecallProbability { get; init; }
    public int BloomLevel { get; init; }
    public double Psi { get; init; }
    public IReadOnlyList<string> RecentErrorTypes { get; init; } = Array.Empty<string>();
    public MasteryQuality QualityQuadrant { get; init; }

    // ── Instructional context ──
    public ScaffoldingLevel ScaffoldingLevel { get; init; }
    public string Methodology { get; init; } = "Socratic";
    public IReadOnlyList<string> MethodHistory { get; init; } = Array.Empty<string>();

    // ── Affect context (from detectors) ──
    public FocusLevel FocusLevel { get; init; } = FocusLevel.Engaged;
    public DisengagementType? DisengagementType { get; init; }
    public ConfusionState ConfusionState { get; init; } = ConfusionState.NotConfused;

    // ── Behavioral signals (from BusConceptAttempt) ──
    public int BackspaceCount { get; init; }
    public int AnswerChangeCount { get; init; }
    public int ResponseTimeMs { get; init; }
    public double MedianResponseTimeMs { get; init; }

    // ── Question difficulty (from PublishedQuestion.Difficulty) ──
    public float? QuestionDifficulty { get; init; }
}
