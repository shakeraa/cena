// =============================================================================
// Cena Platform -- Session Lifecycle REST Endpoints (SES-002)
// Student-facing REST endpoints for session history, resume, and replay.
// All reads go directly to Marten; resume sends a NATS command to the actor.
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NATS.Client.Core;

namespace Cena.Api.Host.Endpoints;

public static class SessionEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions")
            .WithTags("Sessions")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        // GET /api/sessions — list student's sessions (paginated, filterable)
        group.MapGet("/", async (
            HttpContext ctx,
            IDocumentStore store,
            string? subject,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var validPage     = Math.Max(1, page ?? 1);
            var validPageSize = Math.Clamp(pageSize ?? 20, 1, 100);

            await using var session = store.QuerySession();
            var query = session.Query<TutoringSessionDocument>()
                .Where(d => d.StudentId == studentId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(subject))
                query = query.Where(d => d.Subject == subject);

            if (from.HasValue)
                query = query.Where(d => d.StartedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(d => d.StartedAt <= to.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(d => d.StartedAt)
                .Skip((validPage - 1) * validPageSize)
                .Take(validPageSize)
                .ToListAsync();

            var dtos = items.Select(MapToSummary).ToList();
            return Results.Ok(new SessionListResponse(dtos, totalCount, validPage, validPageSize));
        })
        .WithName("GetStudentSessions");

        // GET /api/sessions/active — check if student has an active (unended) session
        group.MapGet("/active", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var doc = await session.Query<TutoringSessionDocument>()
                .Where(d => d.StudentId == studentId && d.EndedAt == null)
                .OrderByDescending(d => d.StartedAt)
                .FirstOrDefaultAsync();

            if (doc is null)
                return Results.Ok(new ActiveSessionResponse(
                    HasActive: false, SessionId: null, Subject: null, StartedAt: null));

            return Results.Ok(new ActiveSessionResponse(
                HasActive: true,
                SessionId: doc.SessionId,
                Subject: doc.Subject,
                StartedAt: doc.StartedAt));
        })
        .WithName("GetActiveSession");

        // GET /api/sessions/{sessionId} — full session detail
        group.MapGet("/{sessionId}", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var doc = await session.Query<TutoringSessionDocument>()
                .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

            if (doc is null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            // IDOR: ensure the session belongs to the requesting student
            if (doc.StudentId != studentId)
                return Results.Forbid();

            // Count correct turns from ConceptAttempted events for accuracy
            var events = await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "concept_attempted_v1")
                .ToListAsync();

            var sessionEvents = events
                .Where(e => ExtractString(e, "sessionId") == doc.SessionId)
                .ToList();

            var questionsAttempted = sessionEvents.Count;
            var questionsCorrect   = sessionEvents.Count(e => ExtractBool(e, "isCorrect"));
            var accuracy           = questionsAttempted > 0
                ? (double)questionsCorrect / questionsAttempted
                : 0;

            var fatigueScore = sessionEvents.Count > 0
                ? sessionEvents.Average(e => ExtractDouble(e, "fatigueScore"))
                : 0;

            var masteryDeltas = sessionEvents
                .GroupBy(e => ExtractString(e, "conceptId"))
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var ordered  = g.OrderBy(e => e.Timestamp).ToList();
                        var initial  = ExtractDouble(ordered.First(), "priorMastery");
                        var terminal = ExtractDouble(ordered.Last(), "posteriorMastery");
                        return terminal - initial;
                    });

            var durationSeconds = doc.EndedAt.HasValue
                ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
                : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

            var status = doc.EndedAt.HasValue ? "completed" : "active";

            return Results.Ok(new SessionDetailDto(
                Id: doc.Id,
                SessionId: doc.SessionId,
                Subject: doc.Subject,
                ConceptId: doc.ConceptId,
                Methodology: doc.Methodology,
                Status: status,
                QuestionsAttempted: questionsAttempted,
                QuestionsCorrect: questionsCorrect,
                Accuracy: Math.Round(accuracy, 3),
                FatigueScore: Math.Round(fatigueScore, 3),
                DurationSeconds: durationSeconds,
                StartedAt: doc.StartedAt,
                EndedAt: doc.EndedAt,
                MasteryDeltas: masteryDeltas));
        })
        .WithName("GetSessionDetail");

        // GET /api/sessions/{sessionId}/replay — question-by-question replay
        group.MapGet("/{sessionId}/replay", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var doc = await session.Query<TutoringSessionDocument>()
                .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

            if (doc is null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            if (doc.StudentId != studentId)
                return Results.Forbid();

            // Load ConceptAttempted events for this session, ordered by timestamp
            var rawEvents = await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "concept_attempted_v1")
                .ToListAsync();

            var attempts = rawEvents
                .Where(e => ExtractString(e, "sessionId") == doc.SessionId)
                .OrderBy(e => e.Timestamp)
                .Select((e, i) => new QuestionAttemptDto(
                    Sequence: i + 1,
                    QuestionId: ExtractString(e, "questionId"),
                    ConceptId: ExtractString(e, "conceptId"),
                    QuestionType: ExtractString(e, "questionType"),
                    IsCorrect: ExtractBool(e, "isCorrect"),
                    ResponseTimeMs: ExtractInt(e, "responseTimeMs"),
                    HintCountUsed: ExtractInt(e, "hintCountUsed"),
                    WasSkipped: ExtractBool(e, "wasSkipped"),
                    PriorMastery: Math.Round(ExtractDouble(e, "priorMastery"), 4),
                    PosteriorMastery: Math.Round(ExtractDouble(e, "posteriorMastery"), 4),
                    Timestamp: e.Timestamp))
                .ToList();

            return Results.Ok(new SessionReplayDto(
                SessionId: doc.SessionId,
                Subject: doc.Subject,
                Methodology: doc.Methodology,
                StartedAt: doc.StartedAt,
                EndedAt: doc.EndedAt,
                Attempts: attempts));
        })
        .WithName("GetSessionReplay");

        // POST /api/sessions/{sessionId}/resume — resume an interrupted session
        group.MapPost("/{sessionId}/resume", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store,
            INatsConnection nats) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var querySession = store.QuerySession();
            var doc = await querySession.Query<TutoringSessionDocument>()
                .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

            if (doc is null)
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            if (doc.StudentId != studentId)
                return Results.Forbid();

            if (doc.EndedAt.HasValue)
                return Results.Conflict(new { error = $"Session '{sessionId}' is already completed and cannot be resumed" });

            // 24-hour expiry check
            var hoursSinceStart = (DateTimeOffset.UtcNow - doc.StartedAt).TotalHours;
            if (hoursSinceStart > 24)
            {
                // Return 410 Gone with a summary of the expired session
                var durationSeconds = (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;
                return Results.Json(
                    new
                    {
                        error = "Session expired. Cannot resume sessions more than 24 hours after they started.",
                        summary = new
                        {
                            sessionId = doc.SessionId,
                            subject   = doc.Subject,
                            startedAt = doc.StartedAt,
                            turns     = doc.TotalTurns,
                            durationSeconds
                        }
                    },
                    statusCode: 410);
            }

            // Publish resume command to NATS — the actor host will handle it
            var busCmd  = new BusResumeSession(StudentId: studentId, SessionId: doc.SessionId);
            var envelope = BusEnvelope<BusResumeSession>.Create(
                NatsSubjects.SessionResume, busCmd, "api-host");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOpts);
            await nats.PublishAsync(NatsSubjects.SessionResume, bytes);

            return Results.Ok(new
            {
                sessionId  = doc.SessionId,
                subject    = doc.Subject,
                methodology = doc.Methodology,
                questionsAttempted = doc.TotalTurns,
                startedAt  = doc.StartedAt,
                resumedAt  = DateTimeOffset.UtcNow,
                message    = "Resume command published. Connect via SignalR to receive the next question."
            });
        })
        .WithName("ResumeSession");

        return app;
    }

    // ── Helpers ──

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");

    private static SessionSummaryDto MapToSummary(TutoringSessionDocument doc)
    {
        var durationSeconds = doc.EndedAt.HasValue
            ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
            : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

        var status = doc.EndedAt.HasValue ? "completed" : "active";

        return new SessionSummaryDto(
            Id: doc.Id,
            SessionId: doc.SessionId,
            Subject: doc.Subject,
            ConceptId: doc.ConceptId,
            Methodology: doc.Methodology,
            Status: status,
            TurnCount: doc.TotalTurns,
            DurationSeconds: durationSeconds,
            StartedAt: doc.StartedAt,
            EndedAt: doc.EndedAt);
    }

    private static int ExtractInt(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return 0;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.TryGetInt32(out var val) ? val : 0;
        }
        catch { /* best-effort */ }
        return 0;
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
                return prop.TryGetDouble(out var val) ? val : 0;
        }
        catch { /* best-effort */ }
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

public sealed record SessionListResponse(
    IReadOnlyList<SessionSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SessionSummaryDto(
    string Id,
    string SessionId,
    string Subject,
    string ConceptId,
    string Methodology,
    string Status,
    int TurnCount,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record ActiveSessionResponse(
    bool HasActive,
    string? SessionId,
    string? Subject,
    DateTimeOffset? StartedAt);

public sealed record SessionDetailDto(
    string Id,
    string SessionId,
    string Subject,
    string ConceptId,
    string Methodology,
    string Status,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double Accuracy,
    double FatigueScore,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyDictionary<string, double> MasteryDeltas);

public sealed record SessionReplayDto(
    string SessionId,
    string Subject,
    string Methodology,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<QuestionAttemptDto> Attempts);

public sealed record QuestionAttemptDto(
    int Sequence,
    string QuestionId,
    string ConceptId,
    string QuestionType,
    bool IsCorrect,
    int ResponseTimeMs,
    int HintCountUsed,
    bool WasSkipped,
    double PriorMastery,
    double PosteriorMastery,
    DateTimeOffset Timestamp);
