// =============================================================================
// Cena Platform -- Plan/Review/Recommendations DTOs (STB-02)
// Student daily plan and learning recommendations
// =============================================================================

namespace Cena.Api.Contracts.Plan;

/// <summary>Today's learning plan (STB-02)</summary>
public record TodaysPlanDto(
    int DailyGoalMinutes,
    int CompletedMinutes,
    PlanBlock? NextBlock);

/// <summary>A single block in the day's plan</summary>
public record PlanBlock(
    string Subject,
    int EstimatedMinutes);

/// <summary>Review items due for spaced repetition (STB-02 Phase 1: stub)</summary>
public record ReviewDueDto(
    int Count,
    DateTime? OldestDueAt,
    string[] SampleSubjects);

/// <summary>Recommended session response (STB-02)</summary>
public record RecommendedSessionsResponse(
    RecommendedSession[] Items);

/// <summary>A single recommended session</summary>
public record RecommendedSession(
    string SessionId,
    string Subject,
    string Reason,
    string Difficulty,    // 'easy' | 'medium' | 'hard'
    int EstimatedMinutes);
