// =============================================================================
// Cena Platform -- Tutoring Admin DTOs
// ADM-017: Response types for tutoring session dashboard, budget, analytics
// =============================================================================

namespace Cena.Api.Contracts.Admin.Tutoring;

// Paginated session list
public sealed record TutoringSessionListResponse(
    IReadOnlyList<TutoringSessionSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

// Summary row for session list table
public sealed record TutoringSessionSummaryDto(
    string Id,
    string StudentId,
    string StudentName,
    string SessionId,
    string ConceptId,
    string Subject,
    string Methodology,
    string Status,       // "active", "completed", "budget_exhausted"
    int TurnCount,
    int DurationSeconds,
    int TokensUsed,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

// Full detail view for a single session
public sealed record TutoringSessionDetailDto(
    string Id,
    string StudentId,
    string StudentName,
    string SessionId,
    string ConceptId,
    string Subject,
    string Methodology,
    string Status,
    int TurnCount,
    int DurationSeconds,
    int TokensUsed,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<ConversationTurnDto> Turns,
    int RagSourcesUsed,
    int SafetyEventsCount,
    int BudgetRemaining);

// Single turn in conversation transcript
public sealed record ConversationTurnDto(
    string Role,
    string MessagePreview,
    DateTimeOffset Timestamp,
    int RagSourceCount);

// Budget status across students
public sealed record TutoringBudgetStatusResponse(
    IReadOnlyList<StudentBudgetDto> Students,
    int TotalTokensToday,
    int TotalStudentsNearLimit);

// Per-student budget usage
public sealed record StudentBudgetDto(
    string StudentId,
    string StudentName,
    int TokensUsedToday,
    int DailyLimit,
    double PercentUsed,
    bool IsExhausted);

// High-level tutoring analytics
public sealed record TutoringAnalyticsDto(
    int ActiveSessionCount,
    double AvgTurnsPerSession,
    double ResolutionRate,
    double AvgBudgetUsagePercent,
    int SessionsToday,
    int SessionsThisWeek);
