// =============================================================================
// Cena Platform -- Analytics Contracts (DB-05)
// Student analytics DTOs.
// =============================================================================

namespace Cena.Api.Contracts.Analytics;

public sealed record StudentProgressSummaryDto(
    string StudentId,
    int TotalSessions,
    int TotalQuestionsAttempted,
    float OverallAccuracy,
    int CurrentStreak,
    int LongestStreak,
    int TotalXp,
    DateTimeOffset? LastSessionAt,
    IReadOnlyDictionary<string, SubjectProgressDto> BySubject);

public sealed record SubjectProgressDto(
    string SubjectId,
    string SubjectName,
    int QuestionsAttempted,
    float Accuracy,
    int MasteryLevel,
    int ConceptsStarted,
    int ConceptsMastered);

public sealed record LearningTimeAnalyticsDto(
    string StudentId,
    int TotalMinutes,
    int ThisWeekMinutes,
    int ThisMonthMinutes,
    float AvgSessionMinutes,
    IReadOnlyList<DailyLearningTimeDto> DailyBreakdown);

public sealed record DailyLearningTimeDto(
    DateTimeOffset Date,
    int Minutes,
    int SessionsCount);
