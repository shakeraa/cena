// =============================================================================
// Cena Platform -- Student Analytics REST Endpoints (SES-002.3)
// Per-student analytics: summary, per-concept mastery, daily progress.
// All queries use Marten async projections — no actor calls for reads.
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Events;
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
                    var state   = kv.Value;
                    var status  = state.IsMastered
                        ? "mastered"
                        : state.PKnown >= 0.7 ? "proficient"
                        : state.PKnown >= 0.4 ? "learning"
                        : "novice";

                    // Subject is derived from the lastMethodology or falls back to concept prefix
                    var subject = state.LastMethodology is not null
                        ? ExtractSubjectFromMethodology(kv.Key)
                        : "";

                    return new ConceptMasteryDto(
                        ConceptId: kv.Key,
                        ConceptName: kv.Key, // Name resolution deferred to concept graph lookup
                        Subject: subject,
                        MasteryLevel: Math.Round(state.PKnown, 4),
                        LastPracticed: state.LastAttemptedAt,
                        Status: status);
                })
                .OrderByDescending(d => d.LastPracticed)
                .ToList();

            return Results.Ok(masteryDtos);
        })
        .WithName("GetAnalyticsMastery");

        // GET /api/analytics/progress?days=30 — daily session counts and accuracy
        group.MapGet("/progress", async (
            HttpContext ctx,
            IDocumentStore store,
            int? days) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var rangeDays = Math.Clamp(days ?? 30, 1, 365);
            var cutoff    = DateTimeOffset.UtcNow.AddDays(-rangeDays);
            var today     = DateTimeOffset.UtcNow.Date;

            await using var session = store.QuerySession();

            // Load sessions in range
            var sessions = await session.Query<TutoringSessionDocument>()
                .Where(d => d.StudentId == studentId && d.StartedAt >= cutoff)
                .ToListAsync();

            // Load concept attempt events in range for accuracy per day
            var allAttemptEvents = await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "concept_attempted_v1" && e.Timestamp >= cutoff)
                .ToListAsync();

            var studentAttempts = allAttemptEvents
                .Where(e => ExtractString(e, "studentId") == studentId)
                .ToList();

            // Build date-keyed dictionaries
            var sessionsByDate = sessions
                .GroupBy(s => s.StartedAt.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var attemptsByDate = studentAttempts
                .GroupBy(e => e.Timestamp.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Generate one entry per day in the range
            var progressPoints = Enumerable
                .Range(0, rangeDays)
                .Select(i => today.AddDays(-rangeDays + 1 + i))
                .Select(date =>
                {
                    var daySessions  = sessionsByDate.TryGetValue(date, out var s) ? s : [];
                    var dayAttempts  = attemptsByDate.TryGetValue(date, out var a) ? a : [];
                    var correct      = dayAttempts.Count(e => ExtractBool(e, "isCorrect"));
                    var accuracy     = dayAttempts.Count > 0
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

        return app;
    }

    // ── Helpers ──

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");

    /// <summary>
    /// Simple XP-to-level formula: each level requires progressively more XP.
    /// Level = floor(sqrt(totalXp / 100)) + 1, capped at 100.
    /// </summary>
    private static int ComputeLevel(int totalXp)
    {
        if (totalXp <= 0) return 1;
        return Math.Min(100, (int)Math.Floor(Math.Sqrt(totalXp / 100.0)) + 1);
    }

    /// <summary>
    /// Best-effort subject extraction from a concept identifier.
    /// Concept IDs often have the form "{subject}_{concept}" or are plain IDs.
    /// </summary>
    private static string ExtractSubjectFromMethodology(string conceptId)
    {
        var parts = conceptId.Split('_', 2);
        return parts.Length > 1 ? parts[0] : "";
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
                return prop.ValueKind == JsonValueKind.True;
        }
        catch { /* best-effort */ }
        return false;
    }

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
        catch { /* best-effort */ }
        return "";
    }

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }
}

// ── Response DTOs ──

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
    string ConceptName,
    string Subject,
    double MasteryLevel,
    DateTimeOffset? LastPracticed,
    string Status);  // "mastered", "proficient", "learning", "novice"

public sealed record DailyProgressDto(
    DateTimeOffset Date,
    int SessionCount,
    int QuestionsAttempted,
    double Accuracy);
