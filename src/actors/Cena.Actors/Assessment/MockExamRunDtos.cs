// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) DTOs
//
// Wire-shape contracts between the student SPA and the
// MockExamRunEndpoints. Aggregate counts, IDs, timer fields — everything
// that is safe to put on a student-bound JSON payload.
//
// What is NOT here: raw question bodies. Those are loaded into the
// runner's session view via a separate /question/{id} read path (or the
// SPA's existing question loader) so the delivery-gate chokepoint
// (ExamSimulationDelivery.AssertDeliverable) stays the single point of
// enforcement for ADR-0043.
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>Request to begin a mock-exam run.</summary>
public sealed record StartMockExamRunRequest(
    /// <summary>Bagrut format code: "806" (math 5U), "807" (math 4U),
    /// "036" (physics). Maps to <see cref="ExamFormat.FromCode"/>.</summary>
    string ExamCode,

    /// <summary>Optional Ministry שאלון code (e.g., "035582") for
    /// display-only provenance. Phase 1A does not use this for question
    /// selection — see follow-up TASK in epic notes.</summary>
    string? PaperCode = null
);

/// <summary>Response after starting a run.</summary>
public sealed record MockExamRunStartedResponse(
    string RunId,
    string ExamCode,
    string? PaperCode,
    int TimeLimitMinutes,
    int PartAQuestionCount,
    int PartBQuestionCount,
    int PartBRequiredCount,
    IReadOnlyList<string> PartAQuestionIds,
    IReadOnlyList<string> PartBQuestionIds,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline);

/// <summary>Snapshot of run state for the runner UI.</summary>
public sealed record MockExamRunStateResponse(
    string RunId,
    string ExamCode,
    string? PaperCode,
    int TimeLimitMinutes,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    bool IsExpired,
    bool IsSubmitted,
    IReadOnlyList<string> PartAQuestionIds,
    IReadOnlyList<string> PartBQuestionIds,
    IReadOnlyList<string> PartBSelectedIds,
    /// <summary>Question IDs the student has submitted an answer for.</summary>
    IReadOnlyList<string> AnsweredIds);

/// <summary>Student picks the Part-B subset they will answer.</summary>
public sealed record SelectPartBRequest(IReadOnlyList<string> SelectedQuestionIds);

/// <summary>Per-question answer submission during the run.</summary>
public sealed record SubmitAnswerRequest(string QuestionId, string Answer);

/// <summary>Final mark sheet returned after submission.</summary>
public sealed record MockExamResultResponse(
    string RunId,
    string ExamCode,
    string? PaperCode,
    int TotalQuestions,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double ScorePercent,
    TimeSpan TimeTaken,
    TimeSpan TimeLimit,
    int VisibilityWarnings,
    IReadOnlyList<MockExamPerQuestionResult> PerQuestion);

/// <summary>Per-question grading line item.</summary>
public sealed record MockExamPerQuestionResult(
    string QuestionId,
    string Section, // "A" | "B"
    bool Attempted,
    bool? Correct,        // null = ungraded (e.g., not selected from Part B)
    string? StudentAnswer,
    string? CanonicalAnswer,
    /// <summary>Engine that decided the verdict (mathnet / sympy / not-graded).</summary>
    string GradingEngine);
