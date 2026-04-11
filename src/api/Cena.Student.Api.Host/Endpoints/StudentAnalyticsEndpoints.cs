// =============================================================================
// Cena Platform -- Student Analytics REST Endpoints (STB-09b)
// Per-student analytics with real rollup projections
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Api.Contracts.Analytics;
using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class StudentAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapStudentAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics")
            .WithTags("Student Analytics")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        // GET /api/analytics/summary — overall stats
        group.MapGet("/summary", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();

            // Load student profile snapshot for XP, streak, session count
            var snapshot = await session.Query<StudentProfileSnapshot>()
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            // Count sessions from TutoringSessionDocument
            var sessions = await session.Query<TutoringSessionDocument>()
                .Where(d => d.StudentId == studentId)
                .ToListAsync();

            var totalSessions = sessions.Count;

            // Load ConceptAttempted events for accuracy stats
            var allAttemptEvents = await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "concept_attempted_v1")
                .ToListAsync();

            var studentAttempts = allAttemptEvents
                .Where(e => ExtractString(e, "studentId") == studentId)
                .ToList();

            var totalQuestionsAttempted = studentAttempts.Count;
            var totalCorrect = studentAttempts.Count(e => ExtractBool(e, "isCorrect"));
            var overallAccuracy = totalQuestionsAttempted > 0
                ? Math.Round((double)totalCorrect / totalQuestionsAttempted, 3)
                : 0;

            return Results.Ok(new AnalyticsSummaryDto(
                TotalSessions: totalSessions,
                TotalQuestionsAttempted: totalQuestionsAttempted,
                OverallAccuracy: overallAccuracy,
                CurrentStreak: snapshot?.CurrentStreak ?? 0,
                LongestStreak: snapshot?.LongestStreak ?? 0,
                TotalXp: snapshot?.TotalXp ?? 0,
                Level: ComputeLevel(snapshot?.TotalXp ?? 0)));
        })
        .WithName("GetAnalyticsSummary");

        // GET /api/analytics/mastery — per-concept mastery levels
        group.MapGet("/mastery", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();

            var snapshot = await session.Query<StudentProfileSnapshot>()
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (snapshot is null)
                return Results.Ok(Array.Empty<ConceptMasteryDto>());

            var masteryDtos = snapshot.ConceptMastery
                .Select(kv =>
                {
                    var state = kv.Value;
                    return new ConceptMasteryDto(
                        ConceptId: kv.Key,
                        MasteryLevel: state.PKnown,
                        IsMastered: state.IsMastered,
                        AttemptsCount: state.TotalAttempts,
                        LastAttemptAt: state.LastAttemptedAt?.UtcDateTime);
                })
                .ToList();

            return Results.Ok(masteryDtos);
        })
        .WithName("GetConceptMastery");

        // GET /api/analytics/progress — daily progress over date range
        group.MapGet("/progress", async (
            HttpContext ctx,
            IDocumentStore store,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var endDate = to?.UtcDateTime ?? DateTime.UtcNow;
            var startDate = from?.UtcDateTime ?? endDate.AddDays(-30);

            await using var session = store.QuerySession();

            // Query sessions within date range
            var sessions = await session.Query<TutoringSessionDocument>()
                .Where(d => d.StudentId == studentId && d.StartedAt >= startDate && d.StartedAt <= endDate)
                .ToListAsync();

            // Query all attempt events for this student in range
            var allAttemptEvents = await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "concept_attempted_v1")
                .ToListAsync();

            var studentAttempts = allAttemptEvents
                .Where(e => ExtractString(e, "studentId") == studentId)
                .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
                .ToList();

            // Group by day
            var days = Enumerable.Range(0, (endDate.Date - startDate.Date).Days + 1)
                .Select(offset => startDate.Date.AddDays(offset))
                .ToList();

            var progressPoints = days
                .Select(date =>
                {
                    var daySessions = sessions
                        .Where(s => s.StartedAt.Date == date)
                        .ToList();

                    var dayAttempts = studentAttempts
                        .Where(e => e.Timestamp.Date == date)
                        .ToList();

                    var correct = dayAttempts.Count(e => ExtractBool(e, "isCorrect"));
                    var accuracy = dayAttempts.Count > 0
                        ? Math.Round((double)correct / dayAttempts.Count, 3)
                        : 0;

                    return new DailyProgressDto(
                        Date: new DateTimeOffset(date, TimeSpan.Zero),
                        SessionCount: daySessions.Count,
                        QuestionsAttempted: dayAttempts.Count,
                        Accuracy: accuracy);
                })
                .ToList();

            return Results.Ok(progressPoints);
        })
        .WithName("GetAnalyticsProgress");

        // GET /api/analytics/time-breakdown — daily learning time (STB-09b)
        group.MapGet("/time-breakdown", async (
            HttpContext ctx,
            IAnalyticsRollupService analytics,
            int? days = 30) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-(days ?? 30) + 1);

            var breakdowns = await analytics.GetTimeRangeAsync(studentId, startDate, endDate);

            // Fill in missing days with zeros
            var items = new List<TimeBreakdownItem>();
            var current = startDate;
            while (current <= endDate)
            {
                var breakdown = breakdowns.FirstOrDefault(b => b.Date.Date == current);
                items.Add(new TimeBreakdownItem(
                    current, 
                    breakdown?.TotalMinutes ?? 0));
                current = current.AddDays(1);
            }

            return Results.Ok(new TimeBreakdownDto(Items: items.ToArray()));
        })
        .WithName("GetTimeBreakdown");

        // GET /api/analytics/time-breakdown/subjects — time by subject (STB-09b)
        group.MapGet("/time-breakdown/subjects", async (
            HttpContext ctx,
            IAnalyticsRollupService analytics,
            int? days = 30) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-(days ?? 30) + 1);

            var breakdowns = await analytics.GetTimeRangeAsync(studentId, startDate, endDate);

            // Aggregate by subject
            var subjectTotals = new Dictionary<string, int>();
            foreach (var b in breakdowns)
            {
                foreach (var kv in b.BySubject)
                {
                    if (!subjectTotals.ContainsKey(kv.Key))
                        subjectTotals[kv.Key] = 0;
                    subjectTotals[kv.Key] += kv.Value;
                }
            }

            return Results.Ok(new { 
                TotalDays = days,
                SubjectBreakdown = subjectTotals,
                TotalMinutes = subjectTotals.Values.Sum()
            });
        })
        .WithName("GetTimeBreakdownBySubject");

        // GET /api/analytics/flow-vs-accuracy — flow score vs accuracy (STB-09b)
        group.MapGet("/flow-vs-accuracy", async (
            HttpContext ctx,
            IAnalyticsRollupService analytics) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var profile = await analytics.GetFlowAccuracyProfileAsync(studentId);

            if (profile == null)
            {
                // Return empty data if no analytics yet
                return Results.Ok(new FlowAccuracyDto(Points: Array.Empty<FlowAccuracyPoint>(),
                    Summary: new FlowAccuracySummary(0, 0, 0, null)));
            }

            // Build points from by-time-of-day data
            var points = new List<FlowAccuracyPoint>();
            foreach (var kv in profile.ByTimeOfDay)
            {
                points.Add(new FlowAccuracyPoint(
                    Category: kv.Key,
                    FlowScore: (int)(kv.Value.AvgFlowScore * 100),
                    Accuracy: (int)(kv.Value.AvgAccuracy * 100),
                    SampleCount: kv.Value.SampleCount));
            }

            // Also add by-focus-state
            foreach (var kv in profile.ByFocusState)
            {
                points.Add(new FlowAccuracyPoint(
                    Category: $"focus:{kv.Key}",
                    FlowScore: (int)(kv.Value.AvgFlowScore * 100),
                    Accuracy: (int)(kv.Value.AvgAccuracy * 100),
                    SampleCount: kv.Value.SampleCount));
            }

            var summary = new FlowAccuracySummary(
                OverallFlowScore: (int)(profile.Overall.AvgFlowScore * 100),
                OverallAccuracy: (int)(profile.Overall.AvgAccuracy * 100),
                TotalSamples: profile.Overall.SampleCount,
                BestTimeOfDay: profile.Overall.BestTimeRecommendation);

            return Results.Ok(new FlowAccuracyDto(Points: points.ToArray(), Summary: summary));
        })
        .WithName("GetFlowVsAccuracy");

        return app;
    }

    // ── Helpers ──

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");

    /// <summary>
    /// Simple XP-to-level formula: each level requires progressively more XP.
    /// Level 1: 0 XP, Level 2: 100 XP, Level 3: 250 XP, Level 4: 450 XP...
    /// Formula: xp = 50 * (level^2 - level)
    /// </summary>
    private static int ComputeLevel(int totalXp)
    {
        if (totalXp <= 0) return 1;
        // Inverse: level = (1 + sqrt(1 + 8*xp/100)) / 2
        var level = (int)((1 + Math.Sqrt(1 + 8 * totalXp / 100.0)) / 2);
        return Math.Max(1, level);
    }

    private static string ExtractString(dynamic evt, string propertyName)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return "";
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(propertyName, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(propertyName), out prop))
                return prop.GetString() ?? "";
        }
        catch { /* best-effort */ }
        return "";
    }

    private static bool ExtractBool(dynamic evt, string propertyName)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return false;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(propertyName, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(propertyName), out prop))
                return prop.ValueKind == JsonValueKind.True;
        }
        catch { /* best-effort */ }
        return false;
    }

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase.Substring(1);
    }
}

// Additional DTOs for STB-09b
public record FlowAccuracySummary(
    int OverallFlowScore,
    int OverallAccuracy,
    int TotalSamples,
    string? BestTimeOfDay);

public record FlowAccuracyPoint(
    string Category,
    int FlowScore,
    int Accuracy,
    int SampleCount);

public record FlowAccuracyDto(
    FlowAccuracyPoint[] Points,
    FlowAccuracySummary? Summary = null);

public record TimeBreakdownItem(DateTime Date, int Minutes);
public record TimeBreakdownDto(TimeBreakdownItem[] Items);
