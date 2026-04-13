// =============================================================================
// Cena Platform — Step Solver Events (STEP-003)
// Domain events for multi-step algebraic/calculus problem solving.
// Students solve step-by-step; each step is CAS-verified before proceeding.
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a new step-solver question is authored or imported.
/// </summary>
public record StepSolverQuestionCreated_V1(
    string QuestionId,
    string Subject,
    string ConceptId,
    string Stem,
    int StepCount,
    string FinalAnswer,
    string AuthoredBy,
    DateTimeOffset CreatedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted when a student submits an expression for a specific step.
/// The CAS engine verifies correctness before this event is appended.
/// </summary>
public record StepAttempted_V1(
    string StudentId,
    string SessionId,
    string QuestionId,
    int StepNumber,
    string SubmittedExpression,
    bool IsCorrect,
    bool UsedHint,
    int HintLevel,
    TimeSpan TimeTaken,
    DateTimeOffset AttemptedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted after the CAS engine verifies a step attempt.
/// Contains the symbolic verification result and any diagnostic info.
/// </summary>
public record StepVerified_V1(
    string StudentId,
    string SessionId,
    string QuestionId,
    int StepNumber,
    string SubmittedExpression,
    string ExpectedExpression,
    bool IsEquivalent,
    string? CasEngine,
    string? SimplifiedForm,
    string? ErrorMessage,
    DateTimeOffset VerifiedAt
) : IDelegatedEvent;
