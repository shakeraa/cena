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
    string? FirstQuestionId); // null in Phase 1, wired in STB-01b

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

public sealed record SessionQuestionDto(
    string QuestionId,
    int QuestionIndex,
    int TotalQuestions,
    string Prompt,
    string QuestionType,  // 'multiple-choice' | 'short-answer' | 'numeric'
    string[] Choices,     // Empty for non-multiple-choice
    string Subject,
    int ExpectedTimeSeconds);

public sealed record SessionAnswerRequest(
    string QuestionId,
    string Answer,
    int TimeSpentMs);

public sealed record SessionAnswerResponseDto(
    bool Correct,
    string Feedback,
    int XpAwarded,
    decimal MasteryDelta,
    string? NextQuestionId);

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
