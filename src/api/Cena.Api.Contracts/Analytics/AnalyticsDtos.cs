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

// ═════════════════════════════════════════════════════════════════════════════
// STB-09: Time Breakdown and Flow vs Accuracy Analytics
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Time breakdown for the last 30 days (STB-09)</summary>
public sealed record TimeBreakdownDto(TimeBreakdownItem[] Items);

/// <summary>Single day time entry (STB-09)</summary>
public sealed record TimeBreakdownItem(
    DateTime Date,
    int Minutes);

/// <summary>Flow vs Accuracy data for the last 7 days (STB-09)</summary>
public sealed record FlowAccuracyDto(FlowAccuracyPoint[] Points);

/// <summary>Single flow/accuracy data point (STB-09)</summary>
public sealed record FlowAccuracyPoint(
    DateTime Timestamp,
    int FlowScore,        // 0-100
    int AccuracyPercent); // 0-100

// ═════════════════════════════════════════════════════════════════════════════
// STB-09b: Additional Analytics DTOs
// ═════════════════════════════════════════════════════════════════════════════

public sealed record AnalyticsSummaryDto(
    int TotalSessions,
    int TotalQuestionsAttempted,
    double OverallAccuracy,
    int CurrentStreak,
    int LongestStreak,
    int TotalXp,
    int Level);

public sealed record ConceptMasteryDto(
    string ConceptId,
    double MasteryLevel,
    bool IsMastered,
    int AttemptsCount,
    DateTime? LastAttemptAt);

public sealed record DailyProgressDto(
    DateTimeOffset Date,
    int SessionCount,
    int QuestionsAttempted,
    double Accuracy);
