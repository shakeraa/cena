// =============================================================================
// Cena Platform -- Student Insights Service
// FIND-data-025: Per-student analytics with tenant scoping, no global scans
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;

namespace Cena.Admin.Api;

public interface IStudentInsightsService
{
    Task<StudentFocusHeatmapResponse?> GetFocusHeatmapAsync(string studentId, ClaimsPrincipal user);
    Task<StudentDegradationCurveResponse?> GetDegradationCurveAsync(string studentId, ClaimsPrincipal user);
    Task<StudentEngagementResponse?> GetEngagementAsync(string studentId, ClaimsPrincipal user);
    Task<StudentErrorTypesResponse?> GetErrorTypesAsync(string studentId, ClaimsPrincipal user);
    Task<StudentHintUsageResponse?> GetHintUsageAsync(string studentId, ClaimsPrincipal user);
    Task<StudentStagnationResponse?> GetStagnationAsync(string studentId, ClaimsPrincipal user);
    Task<StudentSessionPatternsResponse?> GetSessionPatternsAsync(string studentId, ClaimsPrincipal user);
    Task<StudentResponseTimeResponse?> GetResponseTimesAsync(string studentId, ClaimsPrincipal user);
}

public sealed class StudentInsightsService : IStudentInsightsService
{
    private readonly IDocumentStore _store;

    public StudentInsightsService(IDocumentStore store)
    {
        _store = store;
    }

    // ═══════════════════════════════════════════════════════════════
    // TENANT AUTHORIZATION
    // ═══════════════════════════════════════════════════════════════

    private async Task<bool> CanAccessStudentAsync(IQuerySession session, string studentId, string? schoolId)
    {
        // SUPER_ADMIN (schoolId == null) can access any student
        if (schoolId is null)
            return true;

        // Verify student belongs to the caller's school
        var snapshot = await session.Query<StudentProfileSnapshot>()
            .Where(s => s.StudentId == studentId)
            .FirstOrDefaultAsync();

        return snapshot?.SchoolId == schoolId;
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. FOCUS HEATMAP
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentFocusHeatmapResponse?> GetFocusHeatmapAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events instead of global scan
        var studentEvents = await session.Events.FetchStreamAsync(studentId);
        var focusEvents = studentEvents
            .Where(e => e.Data is FocusScoreUpdated_V1)
            .Select(e => (FocusScoreUpdated_V1)e.Data)
            .ToList();

        // Aggregate by day-of-week and hour
        var cells = focusEvents
            .GroupBy(f => (Day: (int)f.Timestamp.DayOfWeek, Hour: f.Timestamp.Hour))
            .Select(g => new FocusHeatmapCell(
                DayOfWeek: g.Key.Day,
                Hour: g.Key.Hour,
                AvgFocusScore: (float)g.Average(f => f.FocusScore * 100),
                SampleCount: g.Count()))
            .ToList();

        return new StudentFocusHeatmapResponse(studentId, cells);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. FOCUS DEGRADATION CURVE
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentDegradationCurveResponse?> GetDegradationCurveAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);
        var focusEvents = studentEvents
            .Where(e => e.Data is FocusScoreUpdated_V1)
            .Select(e => (FocusScoreUpdated_V1)e.Data)
            .OrderBy(f => f.Timestamp)
            .ToList();

        // Group by session (using date as session proxy) and calculate degradation
        var sessionGroups = focusEvents
            .GroupBy(f => f.Timestamp.Date)
            .Where(g => g.Count() >= 3) // Need at least a few points
            .ToList();

        var points = new List<DegradationPoint>();
        int bucket = 0;
        foreach (var sessionGroup in sessionGroups.TakeLast(10)) // Last 10 sessions
        {
            var ordered = sessionGroup.OrderBy(f => f.Timestamp).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                points.Add(new DegradationPoint(
                    MinutesIntoSession: bucket * 2 + i,
                    AvgFocusScore: (float)(ordered[i].FocusScore * 100),
                    SampleSize: 1));
            }
            bucket++;
        }

        return new StudentDegradationCurveResponse(studentId, points);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. ENGAGEMENT: Streak, XP, Badges
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentEngagementResponse?> GetEngagementAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);

        // Streak events
        var streakEvents = studentEvents
            .Where(e => e.Data is StreakUpdated_V1)
            .Select(e => (StreakUpdated_V1)e.Data)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var latestStreak = streakEvents.FirstOrDefault();
        int currentStreak = latestStreak?.CurrentStreak ?? 0;
        int longestStreak = latestStreak?.LongestStreak ?? 0;
        var lastActivity = latestStreak?.Timestamp;

        // XP events
        var xpEvents = studentEvents
            .Where(e => e.Data is XpAwarded_V1)
            .Select(e => (XpAwarded_V1)e.Data)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        int totalXp = xpEvents.FirstOrDefault()?.TotalXp ?? 0;

        var xpByDifficulty = xpEvents
            .GroupBy(e => e.DifficultyLevel?.ToString() ?? "unknown")
            .Select(g => new XpByDifficulty(g.Key, g.Sum(e => e.XpAmount), g.Count()))
            .ToList();

        // Badge events
        var badgeEvents = studentEvents
            .Where(e => e.Data is BadgeEarned_V1)
            .Select(e => (BadgeEarned_V1)e.Data)
            .ToList();

        var studentBadges = badgeEvents
            .Select(b => new BadgeRecord(
                b.BadgeId,
                b.BadgeName,
                b.BadgeCategory?.ToString() ?? "",
                b.Timestamp))
            .ToList();

        return new StudentEngagementResponse(
            StudentId: studentId,
            CurrentStreak: currentStreak,
            LongestStreak: longestStreak,
            LastActivityDate: lastActivity,
            TotalXp: totalXp,
            XpByDifficulty: xpByDifficulty,
            Badges: studentBadges);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. ERROR TYPE DISTRIBUTION
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentErrorTypesResponse?> GetErrorTypesAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);
        var attempts = studentEvents
            .Where(e => e.Data is ConceptAttempted_V1)
            .Select(e => (ConceptAttempted_V1)e.Data)
            .ToList();

        var incorrect = attempts.Where(a => !a.IsCorrect).ToList();

        var byErrorType = incorrect
            .GroupBy(a => a.ErrorType)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new ErrorTypeCount(
                g.Key,
                g.Count(),
                (float)g.Count() / Math.Max(1, incorrect.Count) * 100f))
            .OrderByDescending(e => e.Count)
            .ToList();

        var byConcept = incorrect
            .GroupBy(a => a.ConceptId)
            .Select(g => new ConceptErrorCount(
                g.Key,
                g.Count(),
                g.GroupBy(a => a.ErrorType)
                    .OrderByDescending(eg => eg.Count())
                    .FirstOrDefault()?.Key ?? ""))
            .OrderByDescending(c => c.ErrorCount)
            .Take(10)
            .ToList();

        return new StudentErrorTypesResponse(
            StudentId: studentId,
            TotalAttempts: attempts.Count,
            TotalErrors: incorrect.Count,
            ErrorRate: attempts.Count > 0 ? (float)incorrect.Count / attempts.Count * 100 : 0,
            ByErrorType: byErrorType,
            ByConceptTopErrors: byConcept);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. HINT USAGE PATTERNS
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentHintUsageResponse?> GetHintUsageAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);

        var hints = studentEvents
            .Where(e => e.Data is HintRequested_V1)
            .Select(e => (HintRequested_V1)e.Data)
            .ToList();

        var byLevel = hints
            .GroupBy(h => h.HintLevel)
            .Select(g => new HintLevelCount(
                Level: g.Key,
                Label: g.Key switch { 1 => "Nudge", 2 => "Scaffolded", 3 => "Near-Answer", _ => $"Level {g.Key}" },
                Count: g.Count()))
            .OrderBy(h => h.Level)
            .ToList();

        var byConcept = hints
            .GroupBy(h => h.ConceptId)
            .Select(g => new ConceptHintCount(g.Key, g.Count()))
            .OrderByDescending(c => c.HintCount)
            .Take(10)
            .ToList();

        // Check effectiveness using attempts
        var attempts = studentEvents
            .Where(e => e.Data is ConceptAttempted_V1)
            .Select(e => (ConceptAttempted_V1)e.Data)
            .ToList();

        int hintsUsed = attempts.Sum(a => a.HintCountUsed);
        int hintedCorrect = attempts.Count(a => a.HintCountUsed > 0 && a.IsCorrect);
        int hintedAttempts = attempts.Count(a => a.HintCountUsed > 0);

        float hintEffectiveness = hintedAttempts > 0
            ? (float)hintedCorrect / hintedAttempts * 100f
            : 0;

        return new StudentHintUsageResponse(
            StudentId: studentId,
            TotalHintRequests: hints.Count,
            ByLevel: byLevel,
            ByConcept: byConcept,
            HintEffectivenessPercent: MathF.Round(hintEffectiveness, 1));
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. STAGNATION
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentStagnationResponse?> GetStagnationAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);

        var stagnationEvents = studentEvents
            .Where(e => e.Data is StagnationDetected_V1)
            .Select(e => (StagnationDetected_V1)e.Data)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var concepts = stagnationEvents
            .GroupBy(s => s.ConceptId)
            .Select(g =>
            {
                var latest = g.First();
                return new StagnationConcept
                {
                    ConceptId = g.Key,
                    CompositeScore = latest.CompositeScore,
                    ConsecutiveStagnantSessions = latest.ConsecutiveStagnantSessions,
                    AccuracyPlateau = latest.AccuracyPlateau,
                    ErrorRepetition = latest.ErrorRepetition,
                    LastDetected = latest.Timestamp,
                    TotalDetections = g.Count(),
                };
            })
            .OrderByDescending(c => c.CompositeScore)
            .ToList();

        // Methodology switches for stagnating concepts
        var switches = studentEvents
            .Where(e => e.Data is MethodologySwitched_V1)
            .Select(e => (MethodologySwitched_V1)e.Data)
            .ToList();

        var switchesByConcept = switches
            .GroupBy(s => s.ConceptId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => s.NewMethodology?.ToString()).Distinct().ToList());

        foreach (var concept in concepts)
        {
            if (switchesByConcept.TryGetValue(concept.ConceptId, out var methods))
                concept.AttemptedMethodologies.AddRange(methods.Where(m => m != null)!);
        }

        return new StudentStagnationResponse(
            StudentId: studentId,
            StagnatingConcepts: concepts,
            TotalStagnationEvents: stagnationEvents.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. SESSION PATTERNS (time-of-day, duration, abandonment)
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentSessionPatternsResponse?> GetSessionPatternsAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);

        var starts = studentEvents
            .Where(e => e.Data is SessionStarted_V1)
            .Select(e => (SessionStarted_V1)e.Data)
            .ToList();

        var ends = studentEvents
            .Where(e => e.Data is SessionEnded_V1)
            .Select(e => (SessionEnded_V1)e.Data)
            .ToList();

        // Time-of-day distribution
        var byHour = starts
            .GroupBy(s =>
            {
                var ts = s.Timestamp.ToOffset(TimeSpan.FromHours(3));
                return ts.Hour;
            })
            .Select(g => new SessionTimeSlot($"{g.Key:D2}:00", g.Count()))
            .OrderBy(s => s.TimeSlot)
            .ToList();

        // Day-of-week distribution
        var byDay = starts
            .GroupBy(s =>
            {
                var ts = s.Timestamp.ToOffset(TimeSpan.FromHours(3));
                return ts.DayOfWeek;
            })
            .Select(g => new SessionDayCount(g.Key.ToString(), g.Count()))
            .ToList();

        // End reasons (abandonment analysis)
        var endReasons = ends
            .GroupBy(e => e.EndReason?.ToString() ?? "unknown")
            .Select(g => new EndReasonCount(
                g.Key,
                g.Count(),
                (float)g.Count() / Math.Max(1, ends.Count) * 100f))
            .OrderByDescending(r => r.Count)
            .ToList();

        // Average duration
        var durations = ends
            .Select(e => e.DurationMinutes)
            .Where(d => d > 0)
            .ToList();

        float avgDuration = durations.Count > 0 ? (float)durations.Average() : 0;
        float avgQuestionsPerSession = ends.Count > 0
            ? (float)ends.Average(e => e.QuestionsAttempted)
            : 0;

        int abandonedCount = ends.Count(e => e.EndReason?.ToString() == "abandoned");
        float abandonmentRate = ends.Count > 0 ? (float)abandonedCount / ends.Count * 100f : 0;

        return new StudentSessionPatternsResponse(
            StudentId: studentId,
            TotalSessions: starts.Count,
            AvgDurationMinutes: MathF.Round(avgDuration, 1),
            AvgQuestionsPerSession: MathF.Round(avgQuestionsPerSession, 1),
            AbandonmentRate: MathF.Round(abandonmentRate, 1),
            ByHour: byHour,
            ByDay: byDay,
            EndReasons: endReasons);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. RESPONSE TIME ANOMALIES
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentResponseTimeResponse?> GetResponseTimesAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-025: Verify tenant access
        if (!await CanAccessStudentAsync(session, studentId, schoolId))
            return null;

        // FIND-data-025: Query only this student's events
        var studentEvents = await session.Events.FetchStreamAsync(studentId);
        var attempts = studentEvents
            .Where(e => e.Data is ConceptAttempted_V1)
            .Select(e => (ConceptAttempted_V1)e.Data)
            .OrderBy(e => e.Timestamp)
            .ToList();

        if (attempts.Count < 5)
        {
            return new StudentResponseTimeResponse(studentId, 0, 0, 0, new List<RtTrendPoint>(), new List<RtAnomaly>());
        }

        var rtValues = attempts
            .Select(a => a.ResponseTimeMs)
            .Where(rt => rt > 0)
            .ToList();

        double mean = rtValues.Average();
        double stdDev = Math.Sqrt(rtValues.Select(rt => Math.Pow(rt - mean, 2)).Average());
        double median = rtValues.OrderBy(r => r).ElementAt(rtValues.Count / 2);

        // Trend: group by date
        var trend = attempts
            .GroupBy(a => a.Timestamp.ToString("yyyy-MM-dd"))
            .Select(g => new RtTrendPoint(
                Date: g.Key,
                AvgRtMs: (int)g.Average(a => a.ResponseTimeMs),
                AttemptCount: g.Count()))
            .OrderBy(t => t.Date)
            .TakeLast(30)
            .ToList();

        // Anomalies: >2 standard deviations from mean
        double anomalyThreshold = mean + 2 * stdDev;
        var anomalies = attempts
            .Where(a => a.ResponseTimeMs > anomalyThreshold)
            .Select(a => new RtAnomaly(
                Timestamp: a.Timestamp,
                ResponseTimeMs: a.ResponseTimeMs,
                ConceptId: a.ConceptId,
                ExpectedRangeMs: $"{(int)(mean - stdDev)}-{(int)(mean + stdDev)}"))
            .TakeLast(20)
            .ToList();

        return new StudentResponseTimeResponse(
            StudentId: studentId,
            MedianRtMs: (int)median,
            MeanRtMs: (int)mean,
            StdDevMs: (int)stdDev,
            Trend: trend,
            Anomalies: anomalies);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// RESPONSE DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record StudentFocusHeatmapResponse(string StudentId, IReadOnlyList<FocusHeatmapCell> Cells);
public record FocusHeatmapCell(int DayOfWeek, int Hour, float AvgFocusScore, int SampleCount);

public record StudentDegradationCurveResponse(string StudentId, IReadOnlyList<DegradationPoint> Points);
public record DegradationPoint(int MinutesIntoSession, float AvgFocusScore, int SampleSize);

public record StudentEngagementResponse(
    string StudentId,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset? LastActivityDate,
    int TotalXp,
    IReadOnlyList<XpByDifficulty> XpByDifficulty,
    IReadOnlyList<BadgeRecord> Badges);
public record XpByDifficulty(string DifficultyLevel, int TotalXp, int Count);
public record BadgeRecord(string BadgeId, string BadgeName, string BadgeCategory, DateTimeOffset EarnedAt);

public record StudentErrorTypesResponse(
    string StudentId,
    int TotalAttempts,
    int TotalErrors,
    float ErrorRate,
    IReadOnlyList<ErrorTypeCount> ByErrorType,
    IReadOnlyList<ConceptErrorCount> ByConceptTopErrors);
public record ErrorTypeCount(string ErrorType, int Count, float Percentage);
public record ConceptErrorCount(string ConceptId, int ErrorCount, string TopErrorType);

public record StudentHintUsageResponse(
    string StudentId,
    int TotalHintRequests,
    IReadOnlyList<HintLevelCount> ByLevel,
    IReadOnlyList<ConceptHintCount> ByConcept,
    float HintEffectivenessPercent);
public record HintLevelCount(int Level, string Label, int Count);
public record ConceptHintCount(string ConceptId, int HintCount);

public record StudentStagnationResponse(
    string StudentId,
    IReadOnlyList<StagnationConcept> StagnatingConcepts,
    int TotalStagnationEvents);
public class StagnationConcept
{
    public string ConceptId { get; set; } = "";
    public double CompositeScore { get; set; }
    public int ConsecutiveStagnantSessions { get; set; }
    public double AccuracyPlateau { get; set; }
    public double ErrorRepetition { get; set; }
    public DateTimeOffset LastDetected { get; set; }
    public int TotalDetections { get; set; }
    public List<string> AttemptedMethodologies { get; set; } = new();
}

public record StudentSessionPatternsResponse(
    string StudentId,
    int TotalSessions,
    float AvgDurationMinutes,
    float AvgQuestionsPerSession,
    float AbandonmentRate,
    IReadOnlyList<SessionTimeSlot> ByHour,
    IReadOnlyList<SessionDayCount> ByDay,
    IReadOnlyList<EndReasonCount> EndReasons);
public record SessionTimeSlot(string TimeSlot, int Count);
public record SessionDayCount(string DayOfWeek, int Count);
public record EndReasonCount(string Reason, int Count, float Percentage);

public record StudentResponseTimeResponse(
    string StudentId,
    int MedianRtMs,
    int MeanRtMs,
    int StdDevMs,
    IReadOnlyList<RtTrendPoint> Trend,
    IReadOnlyList<RtAnomaly> Anomalies);
public record RtTrendPoint(string Date, int AvgRtMs, int AttemptCount);
public record RtAnomaly(DateTimeOffset Timestamp, int ResponseTimeMs, string ConceptId, string ExpectedRangeMs);
