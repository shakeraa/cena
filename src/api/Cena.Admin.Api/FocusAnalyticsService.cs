// =============================================================================
// Cena Platform -- Focus Analytics Service
// ADM-006: Focus & attention analytics implementation
// =============================================================================

using System.Text.Json;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IFocusAnalyticsService
{
    Task<FocusOverviewResponse> GetOverviewAsync(string? classId);
    Task<StudentFocusDetailResponse?> GetStudentFocusAsync(string studentId);
    Task<ClassFocusResponse?> GetClassFocusAsync(string classId);
    Task<FocusDegradationResponse> GetDegradationCurveAsync();
    Task<FocusExperimentsResponse> GetExperimentsAsync();
    Task<StudentsNeedingAttentionResponse> GetStudentsNeedingAttentionAsync();
    Task<FocusTimelineResponse> GetStudentTimelineAsync(string studentId, string period);
    Task<ClassHeatmapResponse> GetClassHeatmapAsync(string classId);
}

public sealed class FocusAnalyticsService : IFocusAnalyticsService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<FocusAnalyticsService> _logger;

    public FocusAnalyticsService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<FocusAnalyticsService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<FocusOverviewResponse> GetOverviewAsync(string? classId)
    {
        await using var session = _store.QuerySession();

        var now = DateTimeOffset.UtcNow;
        var today = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var since30d = today.AddDays(-30);
        var since14d = today.AddDays(-14);
        var since7d = today.AddDays(-7);
        var since1h = now.AddHours(-1);

        // Query focus score events from last 30 days
        var focusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var mindWanderingEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "mind_wandering_detected__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var microbreaksTaken = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "microbreak_taken__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var microbreaksSkipped = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "microbreak_skipped__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        // Active sessions: sessions started in the last hour
        var recentSessionsStarted = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_started__v1")
            .Where(e => e.Timestamp >= since1h)
            .ToListAsync();

        // Calculate overview metrics
        var focusScores = focusEvents
            .Select(e => ExtractDouble(e, "focusScore"))
            .Where(s => s > 0)
            .ToList();

        var avgFocusScore = focusScores.Count > 0
            ? (float)(focusScores.Average() * 100)
            : 0f;

        var mindWanderingRate = focusEvents.Count > 0
            ? (float)mindWanderingEvents.Count / focusEvents.Count * 100f
            : 0f;

        var totalMicrobreakResponses = microbreaksTaken.Count + microbreaksSkipped.Count;
        var microbreakCompliance = totalMicrobreakResponses > 0
            ? (float)microbreaksTaken.Count / totalMicrobreakResponses * 100f
            : 0f;

        var activeStudents = recentSessionsStarted
            .Select(e => ExtractString(e, "studentId"))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .Count();

        // Build 7-day trend
        var trend = new List<FocusTrendPoint>();
        for (int i = 6; i >= 0; i--)
        {
            var dayStart = today.AddDays(-i);
            var dayEnd = dayStart.AddDays(1);

            var dayFocusScores = focusEvents
                .Where(e => e.Timestamp >= dayStart && e.Timestamp < dayEnd)
                .Select(e => ExtractDouble(e, "focusScore"))
                .Where(s => s > 0)
                .ToList();

            var daySessionCount = focusEvents
                .Where(e => e.Timestamp >= dayStart && e.Timestamp < dayEnd)
                .Select(e => ExtractString(e, "sessionId"))
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .Count();

            var dayAvg = dayFocusScores.Count > 0
                ? (float)(dayFocusScores.Average() * 100)
                : 0f;

            trend.Add(new FocusTrendPoint(
                dayStart.ToString("yyyy-MM-dd"),
                MathF.Round(dayAvg, 1),
                daySessionCount));
        }

        return new FocusOverviewResponse(
            AvgFocusScore: MathF.Round(avgFocusScore, 1),
            MindWanderingRate: MathF.Round(mindWanderingRate, 1),
            MicrobreakCompliance: MathF.Round(microbreakCompliance, 1),
            ActiveStudents: activeStudents,
            Trend: trend);
    }

    public async Task<StudentFocusDetailResponse?> GetStudentFocusAsync(string studentId)
    {
        await using var session = _store.QuerySession();

        var now = DateTimeOffset.UtcNow;
        var today = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var since30d = today.AddDays(-30);
        var since7d = today.AddDays(-7);

        // Query all focus events for this student
        var allFocusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var studentFocusEvents = allFocusEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        if (studentFocusEvents.Count == 0)
            return null;

        // Session events
        var allSessionStarted = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_started__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var allSessionEnded = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_ended__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var studentSessionStarts = allSessionStarted
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var studentSessionEnds = allSessionEnded
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        // Build sessions from session_started/session_ended pairs
        var sessions = studentSessionStarts
            .Select(startEvt =>
            {
                var sessId = ExtractString(startEvt, "sessionId");
                var endEvt = studentSessionEnds
                    .FirstOrDefault(e => ExtractString(e, "sessionId") == sessId);

                var sessionFocusScores = studentFocusEvents
                    .Where(e => ExtractString(e, "sessionId") == sessId)
                    .Select(e => (float)(ExtractDouble(e, "focusScore") * 100))
                    .ToList();

                var avgScore = sessionFocusScores.Count > 0
                    ? sessionFocusScores.Average() : 0f;
                var minScore = sessionFocusScores.Count > 0
                    ? sessionFocusScores.Min() : 0f;
                var maxScore = sessionFocusScores.Count > 0
                    ? sessionFocusScores.Max() : 0f;

                var endedAt = endEvt?.Timestamp;
                var durationMin = endedAt.HasValue
                    ? (int)(endedAt.Value - startEvt.Timestamp).TotalMinutes
                    : (int)(now - startEvt.Timestamp).TotalMinutes;

                return new FocusSession(
                    SessionId: sessId,
                    StartedAt: startEvt.Timestamp,
                    EndedAt: endedAt,
                    AvgFocusScore: MathF.Round(avgScore, 1),
                    MinFocusScore: MathF.Round(minScore, 1),
                    MaxFocusScore: MathF.Round(maxScore, 1),
                    DurationMinutes: Math.Max(1, durationMin));
            })
            .OrderByDescending(s => s.StartedAt)
            .ToList();

        // Mind wandering events
        var allMindWandering = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "mind_wandering_detected__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var studentMindWandering = allMindWandering
            .Where(e => ExtractString(e, "studentId") == studentId)
            .Select(e => new MindWanderingEvent(
                Timestamp: e.Timestamp,
                FocusScoreAtEvent: (float)(ExtractDouble(e, "focusScore") * 100),
                Context: ExtractString(e, "context"),
                Trigger: ExtractString(e, "trigger")))
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        // Microbreak history
        var allTaken = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "microbreak_taken__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var allSkipped = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "microbreak_skipped__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        var studentMicrobreaks = new List<MicrobreakRecord>();

        foreach (var evt in allTaken.Where(e => ExtractString(e, "studentId") == studentId))
        {
            var suggestedAtStr = ExtractString(evt, "suggestedAt");
            var suggestedAt = DateTimeOffset.TryParse(suggestedAtStr, out var sa)
                ? sa : evt.Timestamp;
            var durationSec = (int)ExtractDouble(evt, "durationSeconds");

            studentMicrobreaks.Add(new MicrobreakRecord(
                SuggestedAt: suggestedAt,
                WasTaken: true,
                TakenAt: evt.Timestamp,
                DurationSeconds: durationSec > 0 ? durationSec : 60));
        }

        foreach (var evt in allSkipped.Where(e => ExtractString(e, "studentId") == studentId))
        {
            studentMicrobreaks.Add(new MicrobreakRecord(
                SuggestedAt: evt.Timestamp,
                WasTaken: false,
                TakenAt: null,
                DurationSeconds: 0));
        }

        studentMicrobreaks = studentMicrobreaks
            .OrderByDescending(m => m.SuggestedAt)
            .ToList();

        // Calculate averages
        var scores7d = studentFocusEvents
            .Where(e => e.Timestamp >= since7d)
            .Select(e => ExtractDouble(e, "focusScore"))
            .Where(s => s > 0)
            .ToList();

        var scores30d = studentFocusEvents
            .Select(e => ExtractDouble(e, "focusScore"))
            .Where(s => s > 0)
            .ToList();

        var avg7d = scores7d.Count > 0 ? (float)(scores7d.Average() * 100) : 0f;
        var avg30d = scores30d.Count > 0 ? (float)(scores30d.Average() * 100) : 0f;

        // Determine chronotype from session start times
        var morningCount = studentSessionStarts
            .Count(e => e.Timestamp.Hour >= 6 && e.Timestamp.Hour < 12);
        var eveningCount = studentSessionStarts
            .Count(e => e.Timestamp.Hour >= 17 && e.Timestamp.Hour < 23);

        var chronotype = morningCount > eveningCount ? "morning"
            : eveningCount > morningCount ? "evening" : "neutral";

        var optimalTime = chronotype switch
        {
            "morning" => "9:00 AM - 12:00 PM",
            "evening" => "6:00 PM - 9:00 PM",
            _ => "10:00 AM - 2:00 PM"
        };

        var chronoText = chronotype switch
        {
            "morning" => "Based on your focus patterns, you perform best during morning hours.",
            "evening" => "Based on your focus patterns, you perform best during evening hours.",
            _ => "Your focus patterns are fairly consistent throughout the day."
        };

        return new StudentFocusDetailResponse(
            StudentId: studentId,
            StudentName: $"Student {studentId}",
            AvgFocusScore7d: MathF.Round(avg7d, 1),
            AvgFocusScore30d: MathF.Round(avg30d, 1),
            Sessions: sessions,
            MindWanderingEvents: studentMindWandering,
            MicrobreakHistory: studentMicrobreaks,
            Chronotype: new ChronotypeRecommendation(
                DetectedChronotype: chronotype,
                OptimalStudyTime: optimalTime,
                RecommendationText: chronoText));
    }

    public async Task<ClassFocusResponse?> GetClassFocusAsync(string classId)
    {
        // Class-level grouping requires class membership data not in events;
        // keeping mock implementation until class roster integration is available.
        var random = new Random(classId.GetHashCode());
        var students = new List<StudentFocusSummary>();

        for (int i = 0; i < 25; i++)
        {
            var score = 50f + random.NextSingle() * 45f;
            students.Add(new StudentFocusSummary(
                StudentId: $"stu-{i}",
                StudentName: $"Student {i + 1}",
                AvgFocusScore: score,
                Trend: random.NextSingle() > 0.6 ? "improving" : (random.NextSingle() > 0.5 ? "stable" : "declining"),
                NeedsAttention: score < 60f));
        }

        var timeSlots = new List<TimeSlotFocus>
        {
            new("9:00-10:00", 78f, 25),
            new("10:00-11:00", 82f, 25),
            new("11:00-12:00", 75f, 24),
            new("14:00-15:00", 68f, 23),
            new("15:00-16:00", 65f, 22),
        };

        var subjects = new List<SubjectFocus>
        {
            new("Math", 74f, 120),
            new("Physics", 71f, 95),
        };

        return new ClassFocusResponse(
            ClassId: classId,
            ClassName: $"Class {classId}",
            ClassAvgFocus: students.Average(s => s.AvgFocusScore),
            Students: students,
            FocusByTimeSlot: timeSlots,
            FocusBySubject: subjects);
    }

    public async Task<FocusDegradationResponse> GetDegradationCurveAsync()
    {
        await using var session = _store.QuerySession();

        var since30d = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(-30);

        var focusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated__v1")
            .Where(e => e.Timestamp >= since30d)
            .ToListAsync();

        // Group by questionNumber to build degradation curve
        var grouped = focusEvents
            .Select(e => new
            {
                QuestionNumber = (int)ExtractDouble(e, "questionNumber"),
                FocusScore = ExtractDouble(e, "focusScore")
            })
            .Where(x => x.FocusScore > 0)
            .GroupBy(x => x.QuestionNumber)
            .OrderBy(g => g.Key)
            .Select(g => new DegradationPoint(
                MinutesIntoSession: g.Key,
                AvgFocusScore: MathF.Round((float)(g.Average(x => x.FocusScore) * 100), 1),
                SampleSize: g.Count()))
            .ToList();

        // If no real data, return empty curve
        if (grouped.Count == 0)
        {
            return new FocusDegradationResponse(new List<DegradationPoint>());
        }

        return new FocusDegradationResponse(grouped);
    }

    public async Task<FocusExperimentsResponse> GetExperimentsAsync()
    {
        // Experiments are managed via a separate configuration system;
        // keeping static data until experiment management is implemented.
        var experiments = new List<FocusExperiment>
        {
            new(
                ExperimentId: "exp-001",
                Name: "Microbreak Interval Test",
                Status: "completed",
                StartedAt: DateTimeOffset.UtcNow.AddDays(-30),
                EndedAt: DateTimeOffset.UtcNow.AddDays(-15),
                Variants: new List<ExperimentVariant>
                {
                    new("v1", "15-min intervals", 48),
                    new("v2", "20-min intervals", 52),
                    new("control", "No breaks", 50)
                },
                Results: new ExperimentMetrics(
                    FocusScoreDelta: 8.5f,
                    CompletionRateDelta: 12.3f,
                    TimeOnTaskDelta: 5.2f,
                    IsStatisticallySignificant: true)),
            new(
                ExperimentId: "exp-002",
                Name: "Socratic vs Worked Examples",
                Status: "running",
                StartedAt: DateTimeOffset.UtcNow.AddDays(-10),
                EndedAt: null,
                Variants: new List<ExperimentVariant>
                {
                    new("v1", "Socratic dialogue", 35),
                    new("control", "Worked examples", 38)
                },
                Results: null)
        };

        return new FocusExperimentsResponse(experiments);
    }

    public async Task<StudentsNeedingAttentionResponse> GetStudentsNeedingAttentionAsync()
    {
        await using var session = _store.QuerySession();

        var since7d = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(-7);

        var focusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated__v1")
            .Where(e => e.Timestamp >= since7d)
            .ToListAsync();

        // Group by studentId, find students with avg < 0.45
        var alerts = focusEvents
            .Select(e => new
            {
                StudentId = ExtractString(e, "studentId"),
                FocusScore = ExtractDouble(e, "focusScore")
            })
            .Where(x => !string.IsNullOrEmpty(x.StudentId) && x.FocusScore > 0)
            .GroupBy(x => x.StudentId)
            .Where(g => g.Average(x => x.FocusScore) < 0.45)
            .Select(g =>
            {
                var avgScore = (float)(g.Average(x => x.FocusScore) * 100);
                return new StudentAttentionAlert(
                    StudentId: g.Key,
                    StudentName: $"Student {g.Key}",
                    ClassId: "unknown",
                    AlertType: "low_focus",
                    CurrentScore: MathF.Round(avgScore, 1),
                    BaselineScore: 70f,
                    Recommendation: avgScore < 30f
                        ? "Consider shorter study sessions with more frequent breaks"
                        : "Review study environment and time of day");
            })
            .OrderBy(a => a.CurrentScore)
            .ToList();

        return new StudentsNeedingAttentionResponse(alerts);
    }

    public async Task<FocusTimelineResponse> GetStudentTimelineAsync(string studentId, string period)
    {
        await using var session = _store.QuerySession();

        var days = period switch { "30d" => 30, "14d" => 14, _ => 7 };
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var since = today.AddDays(-days);

        var focusEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "focus_score_updated__v1")
            .Where(e => e.Timestamp >= since)
            .ToListAsync();

        var mindWanderingEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "mind_wandering_detected__v1")
            .Where(e => e.Timestamp >= since)
            .ToListAsync();

        var microbreakTaken = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "microbreak_taken__v1")
            .Where(e => e.Timestamp >= since)
            .ToListAsync();

        // Filter to this student
        var studentFocus = focusEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var studentMW = mindWanderingEvents
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        var studentBreaks = microbreakTaken
            .Where(e => ExtractString(e, "studentId") == studentId)
            .ToList();

        // Group by day
        var points = new List<FocusTimelinePoint>();
        for (int i = days; i >= 0; i--)
        {
            var dayStart = today.AddDays(-i);
            var dayEnd = dayStart.AddDays(1);

            var dayScores = studentFocus
                .Where(e => e.Timestamp >= dayStart && e.Timestamp < dayEnd)
                .Select(e => ExtractDouble(e, "focusScore"))
                .Where(s => s > 0)
                .ToList();

            var dayMW = studentMW
                .Count(e => e.Timestamp >= dayStart && e.Timestamp < dayEnd);

            var dayBreaks = studentBreaks
                .Count(e => e.Timestamp >= dayStart && e.Timestamp < dayEnd);

            var dayAvg = dayScores.Count > 0
                ? (float)(dayScores.Average() * 100)
                : 0f;

            points.Add(new FocusTimelinePoint(
                dayStart,
                MathF.Round(dayAvg, 1),
                dayMW,
                dayBreaks));
        }

        return new FocusTimelineResponse(studentId, period, points);
    }

    public async Task<ClassHeatmapResponse> GetClassHeatmapAsync(string classId)
    {
        // Class heatmap requires class membership data not in events;
        // keeping mock implementation until class roster integration is available.
        var random = new Random(classId.GetHashCode());
        var hours = new[] { "08:00", "09:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00" };
        var days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };

        var cells = new List<HeatmapCell>();
        foreach (var day in days)
        {
            foreach (var hour in hours)
            {
                cells.Add(new HeatmapCell(day, hour, 50f + random.NextSingle() * 40f, random.Next(5, 25)));
            }
        }

        return new ClassHeatmapResponse(classId, cells);
    }

    // -------------------------------------------------------------------------
    // Helpers for extracting fields from Marten raw event Data
    // -------------------------------------------------------------------------

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

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }
}
