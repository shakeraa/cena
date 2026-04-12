// =============================================================================
// Cena Platform -- Session API Contracts (DB-05)
// Shared DTOs for session lifecycle endpoints.
// =============================================================================

namespace Cena.Api.Contracts.Sessions;

public sealed record SessionListResponse(
    IReadOnlyList<SessionSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SessionSummaryDto(
    string Id,
    string SessionId,
    string Subject,
    string ConceptId,
    string Methodology,
    string Status,
    int TurnCount,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record ActiveSessionResponse(
    bool HasActive,
    string? SessionId,
    string? Subject,
    DateTimeOffset? StartedAt);

public sealed record SessionDetailDto(
    string Id,
    string SessionId,
    string Subject,
    string ConceptId,
    string Methodology,
    string Status,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double Accuracy,
    double FatigueScore,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyDictionary<string, double> MasteryDeltas);

public sealed record SessionReplayDto(
    string SessionId,
    string Subject,
    string Methodology,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<QuestionAttemptDto> Attempts);

public sealed record QuestionAttemptDto(
    int Sequence,
    string QuestionId,
    string ConceptId,
    string QuestionType,
    bool IsCorrect,
    int ResponseTimeMs,
    int HintCountUsed,
    bool WasSkipped,
    double PriorMastery,
    double PosteriorMastery,
    DateTimeOffset Timestamp);

// =============================================================================
// STB-01: Session Start + Active Session DTOs
// =============================================================================

public sealed record SessionStartRequest(
    string[] Subjects,
    int DurationMinutes,     // 5 | 10 | 15 | 30 | 45 | 60
    string Mode);            // 'practice' | 'challenge' | 'review' | 'diagnostic'

public sealed record SessionStartResponse(
    string SessionId,
    string HubGroupName,     // for SignalR subscription: "session-{sessionId}"
    string? FirstQuestionId); // FIND-pedagogy-016: seeded by AdaptiveQuestionPool on session start

public sealed record ActiveSessionDto(
    string SessionId,
    string[] Subjects,
    string Mode,
    DateTime StartedAt,
    int DurationMinutes,
    int ProgressPercent,
    string? CurrentQuestionId);

// =============================================================================
// STB-01b: Session Question + Answer DTOs
// =============================================================================

/// <summary>
/// In-session question DTO served to the Vue student app.
///
/// FIND-pedagogy-006 — Scaffolding fields. The Vue REST path now computes
/// <see cref="ScaffoldingLevel"/> from the student's current mastery on
/// this question's concept so novices see a worked example, intermediate
/// students get a hint budget, and experts get independent practice.
///
/// The level is a plain string to keep the shared contract assembly free
/// of a reference to <c>Cena.Actors</c>. The server converts the enum via
/// ToString() and the TypeScript client types it as
/// <c>'Full' | 'Partial' | 'HintsOnly' | 'None'</c>.
///
/// Citations:
///   - Sweller, van Merriënboer &amp; Paas (1998), DOI 10.1023/A:1022193728205
///     (worked example effect for novice learners).
///   - Renkl &amp; Atkinson (2003), DOI 10.1207/S15326985EP3801_3
///     (faded examples as transition to independent problem solving).
///   - Kalyuga, Ayres, Chandler &amp; Sweller (2003), DOI 10.1207/S15326985EP3801_4
///     (expertise reversal effect — fading scaffolds for experts).
/// </summary>
public sealed record SessionQuestionDto(
    string QuestionId,
    int QuestionIndex,
    int TotalQuestions,
    string Prompt,
    string QuestionType,  // 'multiple-choice' | 'short-answer' | 'numeric'
    string[] Choices,     // Empty for non-multiple-choice
    string Subject,
    int ExpectedTimeSeconds,
    // FIND-pedagogy-006 — scaffolding derived from real BKT mastery at request time.
    string? ScaffoldingLevel = null,   // "Full" | "Partial" | "HintsOnly" | "None"
    string? WorkedExample = null,      // Authored worked example, only when level == Full
    int HintsAvailable = 0,            // Total hint budget for this scaffolding level
    int HintsRemaining = 0);           // Same as HintsAvailable on first load

/// <summary>
/// FIND-pedagogy-006 — Response for POST /api/sessions/{id}/question/{qid}/hint.
/// A single progressive hint produced by the HintGenerator service.
/// Returns 404 when the student has no remaining hints at the current
/// scaffolding level or when the question is not the one in flight.
/// </summary>
public sealed record SessionHintResponseDto(
    int HintLevel,
    string HintText,
    bool HasMoreHints,
    int HintsRemaining);

/// <summary>
/// FIND-pedagogy-006 — In-session hint usage counter stored on the
/// <c>LearningSessionQueueProjection</c>. Per-question so the budget
/// resets for each new question (matching the actor-side hint budget
/// semantics in <c>LearningSessionActor.HandleHintRequest</c>).
/// </summary>
public sealed record SessionHintUsageDto(string QuestionId, int HintsUsed);

/// <summary>
/// Request body for POST /api/sessions/{sessionId}/question/{questionId}/hint.
/// HintLevel must be between 1 and 3 (inclusive).
/// </summary>
public sealed record SessionHintRequest(int HintLevel);

public sealed record SessionAnswerRequest(
    string QuestionId,
    string Answer,
    int TimeSpentMs);

/// <summary>
/// Response for POST /api/sessions/{id}/answer.
///
/// FIND-pedagogy-001 / FIND-pedagogy-017 — feedback is no longer binary.
/// The <see cref="Feedback"/> field is DEPRECATED: the server ships an empty
/// string since FIND-pedagogy-017. The UI renders its own translated heading
/// via i18n keys. This field is kept for one release for backwards-compat;
/// callers should stop reading it.
///
/// The response carries:
/// - <see cref="Explanation"/>: the authored per-question worked explanation
///   (<c>QuestionDocument.Explanation</c>). Shown on every answer when present.
/// - <see cref="DistractorRationale"/>: the authored rationale for the SPECIFIC
///   wrong option the student chose (<c>QuestionDocument.DistractorRationales</c>).
///   Null on correct answers and null when no per-option rationale exists.
///
/// Both fields are nullable — the UI renders them only when present and falls
/// back gracefully when the question has no authored explanation yet.
///
/// Citations: Hattie &amp; Timperley (2007) "The Power of Feedback";
/// Black &amp; Wiliam (1998) "Assessment and Classroom Learning".
/// </summary>
public sealed record SessionAnswerResponseDto(
    bool Correct,
    [property: Obsolete("FIND-pedagogy-017: UI uses i18n keys for the heading. This field ships empty and will be removed next release.")]
    string Feedback,
    int XpAwarded,
    decimal MasteryDelta,
    string? NextQuestionId,
    string? Explanation = null,
    string? DistractorRationale = null);

public sealed record SessionCompletedDto(
    string SessionId,
    int TotalCorrect,
    int TotalWrong,
    int TotalXpAwarded,
    int AccuracyPercent,
    int DurationSeconds);

// =============================================================================
// STB-01c: Session History DTOs
// =============================================================================

public sealed record SessionHistoryDto(
    string SessionId,
    DateTime StartedAt,
    DateTime? EndedAt,
    string Mode,
    string[] Subjects,
    int TotalQuestionsAttempted,
    int CorrectAnswers,
    double Accuracy,
    int CurrentStreak,
    IReadOnlyList<QuestionHistoryItemDto> QuestionHistory,
    int RemainingInQueue);

public sealed record QuestionHistoryItemDto(
    string QuestionId,
    DateTime AnsweredAt,
    bool IsCorrect,
    int TimeSpentSeconds,
    string? SelectedOption);
