// =============================================================================
// Cena Platform -- Student Insights DTOs
// Per-student analytics: heatmap, degradation, engagement, error types,
// hint usage, stagnation, session patterns, response time anomalies.
// =============================================================================

namespace Cena.Api.Contracts.Admin.Insights;

using Cena.Api.Contracts.Admin.Analytics; // For HeatmapCell, DegradationPoint shared types

// ── Focus Heatmap (per-student) ──
public sealed record StudentFocusHeatmapResponse(
    string StudentId,
    IReadOnlyList<HeatmapCell> Cells);

// ── Focus Degradation Curve (per-student) ──
public sealed record StudentDegradationCurveResponse(
    string StudentId,
    IReadOnlyList<DegradationPoint> Curve);

// ── Engagement: Streak, XP, Badges ──
public sealed record StudentEngagementResponse(
    string StudentId,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset? LastActivityDate,
    int TotalXp,
    IReadOnlyList<XpByDifficulty> XpByDifficulty,
    IReadOnlyList<BadgeRecord> Badges);

public sealed record XpByDifficulty(string DifficultyLevel, int TotalXp, int AttemptCount);
public sealed record BadgeRecord(string BadgeId, string BadgeName, string BadgeCategory, DateTimeOffset EarnedAt);

// ── Error Type Distribution ──
public sealed record StudentErrorTypesResponse(
    string StudentId,
    int TotalAttempts,
    int TotalErrors,
    float ErrorRate,
    IReadOnlyList<ErrorTypeCount> ByErrorType,
    IReadOnlyList<ConceptErrorCount> ByConceptTopErrors);

public sealed record ErrorTypeCount(string ErrorType, int Count, float Percentage);
public sealed record ConceptErrorCount(string ConceptId, int ErrorCount, string DominantErrorType);

// ── Hint Usage Patterns ──
public sealed record StudentHintUsageResponse(
    string StudentId,
    int TotalHintRequests,
    IReadOnlyList<HintLevelCount> ByLevel,
    IReadOnlyList<ConceptHintCount> ByConcept,
    float HintEffectivenessPercent);

public sealed record HintLevelCount(int Level, string Label, int Count);
public sealed record ConceptHintCount(string ConceptId, int HintCount);

// ── Stagnation ──
public sealed record StudentStagnationResponse(
    string StudentId,
    IReadOnlyList<StagnationConcept> StagnatingConcepts,
    int TotalStagnationEvents);

public sealed class StagnationConcept
{
    public string ConceptId { get; init; } = "";
    public double CompositeScore { get; init; }
    public int ConsecutiveStagnantSessions { get; init; }
    public double AccuracyPlateau { get; init; }
    public double ErrorRepetition { get; init; }
    public DateTimeOffset LastDetected { get; init; }
    public int TotalDetections { get; init; }
    public List<string> AttemptedMethodologies { get; init; } = new();
}

// ── Session Patterns ──
public sealed record StudentSessionPatternsResponse(
    string StudentId,
    int TotalSessions,
    float AvgDurationMinutes,
    float AvgQuestionsPerSession,
    float AbandonmentRate,
    IReadOnlyList<SessionTimeSlot> ByHour,
    IReadOnlyList<SessionDayCount> ByDay,
    IReadOnlyList<EndReasonCount> EndReasons);

public sealed record SessionTimeSlot(string TimeSlot, int SessionCount);
public sealed record SessionDayCount(string Day, int SessionCount);
public sealed record EndReasonCount(string Reason, int Count, float Percentage);

// ── Response Time Anomalies ──
public sealed record StudentResponseTimeResponse(
    string StudentId,
    int MedianRtMs,
    int MeanRtMs,
    int StdDevMs,
    IReadOnlyList<RtTrendPoint> Trend,
    IReadOnlyList<RtAnomaly> Anomalies);

public sealed record RtTrendPoint(string Date, int AvgRtMs, int AttemptCount);
public sealed record RtAnomaly(DateTimeOffset Timestamp, int ResponseTimeMs, string ConceptId, string ExpectedRangeMs);
