// =============================================================================
// Cena Platform -- Student Insights Service
// Per-student cross-cutting analytics: session patterns, engagement, error
// types, hint usage, response time anomalies, stagnation, focus heatmap,
// and focus degradation curve.
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IStudentInsightsService
{
    Task<StudentFocusHeatmapResponse> GetFocusHeatmapAsync(string studentId, ClaimsPrincipal user);
    Task<StudentDegradationCurveResponse> GetDegradationCurveAsync(string studentId, ClaimsPrincipal user);
    Task<StudentEngagementResponse> GetEngagementAsync(string studentId, ClaimsPrincipal user);
    Task<StudentErrorTypesResponse> GetErrorTypesAsync(string studentId, ClaimsPrincipal user);
    Task<StudentHintUsageResponse> GetHintUsageAsync(string studentId, ClaimsPrincipal user);
    Task<StudentStagnationResponse> GetStagnationAsync(string studentId, ClaimsPrincipal user);
    Task<StudentSessionPatternsResponse> GetSessionPatternsAsync(string studentId, ClaimsPrincipal user);
    Task<StudentResponseTimeResponse> GetResponseTimesAsync(string studentId, ClaimsPrincipal user);
}

public sealed class StudentInsightsService : IStudentInsightsService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<StudentInsightsService> _logger;

    public StudentInsightsService(IDocumentStore store, ILogger<StudentInsightsService> logger)
    {
        _store = store;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. PER-STUDENT FOCUS HEATMAP (day x hour)
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentFocusHeatmapResponse> GetFocusHeatmapAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var focusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(2000)
            .ToListAsync();

        var studentEvents = focusEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var cells = studentEvents
            .GroupBy(e =>
            {
                var ts = e.Timestamp.ToOffset(TimeSpan.FromHours(3)); // Israel time
                return (Day: ts.DayOfWeek.ToString()[..3], Hour: ts.Hour);
            })
            .Select(g => new HeatmapCell(
                Day: g.Key.Day,
                Hour: $"{g.Key.Hour:D2}:00",
                AvgFocusScore: (float)(g.Average(e => ExtractDouble(e, "focusScore")) * 100),
                StudentCount: g.Count()))
            .OrderBy(c => c.Day)
            .ThenBy(c => c.Hour)
            .ToList();

        return new StudentFocusHeatmapResponse(studentId, cells);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. PER-STUDENT DEGRADATION CURVE
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentDegradationCurveResponse> GetDegradationCurveAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var focusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(2000)
            .ToListAsync();

        var studentEvents = focusEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        // Group by question number (proxy for minutes into session)
        var points = studentEvents
            .GroupBy(e => (int)ExtractDouble(e, "questionNumber"))
            .Where(g => g.Key > 0)
            .Select(g => new DegradationPoint(
                MinutesIntoSession: g.Key * 2, // ~2 min per question estimate
                AvgFocusScore: (float)(g.Average(e => ExtractDouble(e, "focusScore")) * 100),
                SampleSize: g.Count()))
            .OrderBy(p => p.MinutesIntoSession)
            .ToList();

        return new StudentDegradationCurveResponse(studentId, points);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. ENGAGEMENT: Streak, XP, Badges
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentEngagementResponse> GetEngagementAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        // Query streak events
        var streakEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "streak_updated_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(500)
            .ToListAsync();

        var latestStreak = streakEvents
            .FirstOrDefault(e => ExtractString(e, "studentId") == studentId);

        int currentStreak = latestStreak != null ? (int)ExtractDouble(latestStreak, "currentStreak") : 0;
        int longestStreak = latestStreak != null ? (int)ExtractDouble(latestStreak, "longestStreak") : 0;
        var lastActivity = latestStreak?.Timestamp;

        // Query XP events
        var xpEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "xp_awarded_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(2000)
            .ToListAsync();

        var studentXpEvents = xpEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        int totalXp = studentXpEvents.Count > 0
            ? (int)ExtractDouble(studentXpEvents.First(), "totalXp")
            : 0;

        var xpByDifficulty = studentXpEvents
            .GroupBy(e => ExtractString(e, "difficultyLevel"))
            .Select(g => new XpByDifficulty(g.Key, g.Sum(e => (int)ExtractDouble(e, "xpAmount")), g.Count()))
            .ToList();

        // Query badge events
        var badgeEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "badge_earned_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(500)
            .ToListAsync();

        var studentBadges = badgeEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .Select(e => new BadgeRecord(
                ExtractString(e, "badgeId"),
                ExtractString(e, "badgeName"),
                ExtractString(e, "badgeCategory"),
                e.Timestamp))
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

    public async Task<StudentErrorTypesResponse> GetErrorTypesAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var attemptEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(5000)
            .ToListAsync();

        var studentAttempts = attemptEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var incorrect = studentAttempts.Where(e => !ExtractBool(e, "isCorrect")).ToList();

        var byErrorType = incorrect
            .GroupBy(e => ExtractString(e, "errorType"))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new ErrorTypeCount(g.Key, g.Count(),
                (float)g.Count() / Math.Max(1, incorrect.Count) * 100f))
            .OrderByDescending(e => e.Count)
            .ToList();

        var byConcept = incorrect
            .GroupBy(e => ExtractString(e, "conceptId"))
            .Select(g => new ConceptErrorCount(g.Key, g.Count(),
                g.GroupBy(e => ExtractString(e, "errorType"))
                    .OrderByDescending(eg => eg.Count())
                    .First().Key))
            .OrderByDescending(c => c.ErrorCount)
            .Take(10)
            .ToList();

        return new StudentErrorTypesResponse(
            StudentId: studentId,
            TotalAttempts: studentAttempts.Count,
            TotalErrors: incorrect.Count,
            ErrorRate: studentAttempts.Count > 0 ? (float)incorrect.Count / studentAttempts.Count * 100 : 0,
            ByErrorType: byErrorType,
            ByConceptTopErrors: byConcept);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. HINT USAGE PATTERNS
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentHintUsageResponse> GetHintUsageAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var hintEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "hint_requested_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(2000)
            .ToListAsync();

        var studentHints = hintEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var byLevel = studentHints
            .GroupBy(e => (int)ExtractDouble(e, "hintLevel"))
            .Select(g => new HintLevelCount(
                Level: g.Key,
                Label: g.Key switch { 1 => "Nudge", 2 => "Scaffolded", 3 => "Near-Answer", _ => $"Level {g.Key}" },
                Count: g.Count()))
            .OrderBy(h => h.Level)
            .ToList();

        var byConcept = studentHints
            .GroupBy(e => ExtractString(e, "conceptId"))
            .Select(g => new ConceptHintCount(g.Key, g.Count()))
            .OrderByDescending(c => c.HintCount)
            .Take(10)
            .ToList();

        // Check effectiveness: did student get the next question right after a hint?
        var attemptEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(3000)
            .ToListAsync();

        var studentAttempts = attemptEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        int hintsUsed = studentAttempts.Sum(e => (int)ExtractDouble(e, "hintCountUsed"));
        int hintedCorrect = studentAttempts
            .Where(e => (int)ExtractDouble(e, "hintCountUsed") > 0 && ExtractBool(e, "isCorrect"))
            .Count();

        float hintEffectiveness = hintsUsed > 0 ? (float)hintedCorrect / studentAttempts.Count(e => (int)ExtractDouble(e, "hintCountUsed") > 0) * 100f : 0;

        return new StudentHintUsageResponse(
            StudentId: studentId,
            TotalHintRequests: studentHints.Count,
            ByLevel: byLevel,
            ByConcept: byConcept,
            HintEffectivenessPercent: MathF.Round(hintEffectiveness, 1));
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. STAGNATION
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentStagnationResponse> GetStagnationAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var stagnationEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "stagnation_detected_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(1000)
            .ToListAsync();

        var studentStagnation = stagnationEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var concepts = studentStagnation
            .GroupBy(e => ExtractString(e, "conceptId"))
            .Select(g =>
            {
                var latest = g.First(); // most recent
                return new StagnationConcept
                {
                    ConceptId = g.Key,
                    CompositeScore = ExtractDouble(latest, "compositeScore"),
                    ConsecutiveStagnantSessions = (int)ExtractDouble(latest, "consecutiveStagnantSessions"),
                    AccuracyPlateau = ExtractDouble(latest, "accuracyPlateau"),
                    ErrorRepetition = ExtractDouble(latest, "errorRepetition"),
                    LastDetected = latest.Timestamp,
                    TotalDetections = g.Count(),
                };
            })
            .OrderByDescending(c => c.CompositeScore)
            .ToList();

        // Methodology switches for stagnating concepts
        var switchEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "methodology_switched_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(1000)
            .ToListAsync();

        var studentSwitches = switchEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var switchesByConcept = studentSwitches
            .GroupBy(e => ExtractString(e, "conceptId"))
            .ToDictionary(g => g.Key, g => g.Select(e => ExtractString(e, "newMethodology")).Distinct().ToList());

        foreach (var concept in concepts)
        {
            if (switchesByConcept.TryGetValue(concept.ConceptId, out var methods))
                concept.AttemptedMethodologies.AddRange(methods);
        }

        return new StudentStagnationResponse(
            StudentId: studentId,
            StagnatingConcepts: concepts,
            TotalStagnationEvents: studentStagnation.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. SESSION PATTERNS (time-of-day, duration, abandonment)
    // ═══════════════════════════════════════════════════════════════

    public async Task<StudentSessionPatternsResponse> GetSessionPatternsAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var startEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_started_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(2000)
            .ToListAsync();

        var endEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_ended_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(2000)
            .ToListAsync();

        var studentStarts = startEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var studentEnds = endEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        // Time-of-day distribution
        var byHour = studentStarts
            .GroupBy(e =>
            {
                var ts = e.Timestamp.ToOffset(TimeSpan.FromHours(3));
                return ts.Hour;
            })
            .Select(g => new SessionTimeSlot($"{g.Key:D2}:00", g.Count()))
            .OrderBy(s => s.TimeSlot)
            .ToList();

        // Day-of-week distribution
        var byDay = studentStarts
            .GroupBy(e =>
            {
                var ts = e.Timestamp.ToOffset(TimeSpan.FromHours(3));
                return ts.DayOfWeek;
            })
            .Select(g => new SessionDayCount(g.Key.ToString(), g.Count()))
            .ToList();

        // End reasons (abandonment analysis)
        var endReasons = studentEnds
            .GroupBy(e => ExtractString(e, "endReason"))
            .Select(g => new EndReasonCount(g.Key, g.Count(),
                (float)g.Count() / Math.Max(1, studentEnds.Count) * 100f))
            .OrderByDescending(r => r.Count)
            .ToList();

        // Average duration
        var durations = studentEnds
            .Select(e => (int)ExtractDouble(e, "durationMinutes"))
            .Where(d => d > 0)
            .ToList();

        float avgDuration = durations.Count > 0 ? (float)durations.Average() : 0;
        float avgQuestionsPerSession = studentEnds.Count > 0
            ? (float)studentEnds.Average(e => ExtractDouble(e, "questionsAttempted"))
            : 0;

        int abandonedCount = studentEnds.Count(e => ExtractString(e, "endReason") == "abandoned");
        float abandonmentRate = studentEnds.Count > 0 ? (float)abandonedCount / studentEnds.Count * 100f : 0;

        return new StudentSessionPatternsResponse(
            StudentId: studentId,
            TotalSessions: studentStarts.Count,
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

    public async Task<StudentResponseTimeResponse> GetResponseTimesAsync(string studentId, ClaimsPrincipal user)
    {
        await using var session = _store.QuerySession();

        var attemptEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1")
            .OrderByDescending(e => e.Timestamp)
            .Take(5000)
            .ToListAsync();

        var studentAttempts = attemptEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        if (studentAttempts.Count < 5)
        {
            return new StudentResponseTimeResponse(studentId, 0, 0, 0, new List<RtTrendPoint>(), new List<RtAnomaly>());
        }

        var rtValues = studentAttempts
            .Select(e => (int)ExtractDouble(e, "responseTimeMs"))
            .Where(rt => rt > 0)
            .ToList();

        double mean = rtValues.Average();
        double stdDev = Math.Sqrt(rtValues.Select(rt => Math.Pow(rt - mean, 2)).Average());
        double median = rtValues.OrderBy(r => r).ElementAt(rtValues.Count / 2);

        // Trend: group by date
        var trend = studentAttempts
            .GroupBy(e => e.Timestamp.ToString("yyyy-MM-dd"))
            .Select(g => new RtTrendPoint(
                Date: g.Key,
                AvgRtMs: (int)g.Average(e => ExtractDouble(e, "responseTimeMs")),
                AttemptCount: g.Count()))
            .OrderBy(t => t.Date)
            .TakeLast(30)
            .ToList();

        // Anomalies: >2 standard deviations from mean
        double anomalyThreshold = mean + 2 * stdDev;
        var anomalies = studentAttempts
            .Where(e => ExtractDouble(e, "responseTimeMs") > anomalyThreshold)
            .Select(e => new RtAnomaly(
                Timestamp: e.Timestamp,
                ResponseTimeMs: (int)ExtractDouble(e, "responseTimeMs"),
                ConceptId: ExtractString(e, "conceptId"),
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

    // ═══════════════════════════════════════════════════════════════
    // HELPERS (same pattern as FocusAnalyticsService)
    // ═══════════════════════════════════════════════════════════════

    private static string ExtractString(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return "";
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.GetString() ?? "";
        }
        catch { /* best-effort extraction */ }
        return "";
    }

    private static double ExtractDouble(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return 0;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
            {
                return prop.TryGetDouble(out var v) ? v : 0;
            }
        }
        catch { /* best-effort extraction */ }
        return 0;
    }

    private static bool ExtractBool(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return false;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
            {
                return prop.ValueKind == JsonValueKind.True;
            }
        }
        catch { /* best-effort extraction */ }
        return false;
    }

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }
}
