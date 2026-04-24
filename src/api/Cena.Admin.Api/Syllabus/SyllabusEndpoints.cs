// =============================================================================
// Cena Platform — Syllabus + Advancement HTTP Endpoints (RDY-061 Phase 3)
//
// Read-side queries for the syllabus definition + per-student advancement.
// Teacher-override writes go through a distinct path with rationale +
// audit logging. No student data is ever mutated without rationale.
//
// Endpoints (all require authentication; additional policy per route):
//   GET    /api/admin/tracks/{trackId}/syllabus        - ADMIN
//   GET    /api/admin/students/{studentId}/advancement - ADMIN or OWNER
//   POST   /api/admin/students/{studentId}/advancement/ensure-started - ADMIN
//   POST   /api/admin/students/{studentId}/advancement/override - TEACHER+
//   GET    /api/me/advancement                          - any signed-in student
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Advancement;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Syllabus;

public static class SyllabusEndpoints
{
    public static IEndpointRouteBuilder MapSyllabusEndpoints(this IEndpointRouteBuilder app)
    {
        var adminGroup = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        // ── Syllabus definition reads ────────────────────────────────

        // GET /api/admin/tracks/{trackId}/syllabus
        // Returns the ordered chapter list + prereq structure for a track.
        adminGroup.MapGet("/tracks/{trackId}/syllabus", async (
            string trackId,
            IDocumentStore store) =>
        {
            await using var session = store.QuerySession();
            var syllabus = await session.Query<SyllabusDocument>()
                .Where(s => s.TrackId == trackId)
                .FirstOrDefaultAsync();
            if (syllabus is null)
                return Results.NotFound(new CenaError(
                    "CENA_SYLLABUS_NOT_FOUND",
                    $"No syllabus ingested for track {trackId}.",
                    ErrorCategory.NotFound, null, null));

            var chapters = await session.Query<ChapterDocument>()
                .Where(c => c.SyllabusId == syllabus.Id)
                .ToListAsync();
            var orderedChapters = chapters.OrderBy(c => c.Order).ToList();

            return Results.Ok(new SyllabusResponse(
                SyllabusId: syllabus.Id,
                TrackId: syllabus.TrackId,
                Version: syllabus.Version,
                Track: syllabus.Track.ToString(),
                MinistryCodes: syllabus.MinistryCodes,
                TotalExpectedWeeks: syllabus.TotalExpectedWeeks,
                Chapters: orderedChapters.Select(c => new ChapterResponse(
                    Id: c.Id,
                    Order: c.Order,
                    Slug: c.Slug,
                    TitleByLocale: c.TitleByLocale,
                    LearningObjectiveIds: c.LearningObjectiveIds,
                    PrerequisiteChapterIds: c.PrerequisiteChapterIds,
                    ExpectedWeeks: c.ExpectedWeeks,
                    MinistryCode: c.MinistryCode)).ToList()));
        })
        .WithName("GetTrackSyllabus")
        .Produces<SyllabusResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status404NotFound);

        // ── Student advancement ──────────────────────────────────────

        // GET /api/admin/students/{studentId}/advancement?trackId=X
        adminGroup.MapGet("/students/{studentId}/advancement", async (
            string studentId, string trackId,
            IStudentAdvancementService svc,
            IDocumentStore store) =>
        {
            var state = await svc.GetAsync(studentId, trackId);
            if (state is null)
                return Results.NotFound(new CenaError(
                    "CENA_ADVANCEMENT_NOT_STARTED",
                    $"No advancement state for student {studentId} on track {trackId}.",
                    ErrorCategory.NotFound, null, null));

            await using var session = store.QuerySession();
            var chapterOrder = (await session.Query<ChapterDocument>()
                .Where(c => c.SyllabusId == state.SyllabusId)
                .ToListAsync())
                .ToDictionary(c => c.Id, c => c.Order);

            return Results.Ok(new AdvancementResponse(
                AdvancementId: state.Id,
                StudentId: state.StudentId,
                TrackId: state.TrackId,
                SyllabusId: state.SyllabusId,
                SyllabusVersion: state.SyllabusVersion,
                CurrentChapterId: state.CurrentChapterId,
                Chapters: state.ChapterStatuses
                    .OrderBy(kvp => chapterOrder.GetValueOrDefault(kvp.Key, 99))
                    .Select(kvp => new ChapterAdvancementResponse(
                        ChapterId: kvp.Key,
                        Status: kvp.Value.ToString(),
                        LastUpdated: state.ChapterLastUpdated.GetValueOrDefault(kvp.Key),
                        QuestionsAttempted: state.ChapterQuestionsAttempted.GetValueOrDefault(kvp.Key, 0),
                        Retention: state.ChapterRetention.GetValueOrDefault(kvp.Key, 0f))).ToList(),
                CreatedAt: state.CreatedAt,
                LastAdvancedAt: state.LastAdvancedAt));
        })
        .WithName("GetStudentAdvancement");

        // POST /api/admin/students/{studentId}/advancement/ensure-started
        adminGroup.MapPost("/students/{studentId}/advancement/ensure-started", async (
            string studentId, [FromBody] EnsureStartedRequest req,
            IStudentAdvancementService svc) =>
        {
            var state = await svc.EnsureStartedAsync(studentId, req.TrackId);
            return Results.Ok(new { advancementId = state.Id, chaptersCount = state.ChapterStatuses.Count });
        })
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
        .WithName("EnsureAdvancementStarted");

        // POST /api/admin/students/{studentId}/advancement/override
        adminGroup.MapPost("/students/{studentId}/advancement/override", async (
            string studentId, [FromBody] OverrideRequest req,
            ClaimsPrincipal user,
            IStudentAdvancementService svc,
            ILogger<Marker> logger) =>
        {
            if (!Enum.TryParse<ChapterStatus>(req.NewStatus, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new CenaError(
                    ErrorCodes.CENA_INTERNAL_VALIDATION,
                    $"NewStatus must be one of: Locked, Unlocked, InProgress, Mastered, NeedsReview.",
                    ErrorCategory.Validation, null, null));

            var overriddenBy = user.FindFirst("user_id")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "unknown";
            try
            {
                await svc.OverrideChapterStatusAsync(
                    studentId, req.TrackId, req.ChapterId, newStatus, overriddenBy, req.Rationale);
                logger.LogWarning(
                    "[AUDIT] advancement-override student={Student} track={Track} chapter={Chapter} status={Status} by={By} reason={Reason}",
                    studentId, req.TrackId, req.ChapterId, newStatus, overriddenBy, req.Rationale);
                return Results.Ok(new { applied = true });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new CenaError(
                    ErrorCodes.CENA_INTERNAL_VALIDATION, ex.Message,
                    ErrorCategory.Validation, null, null));
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new CenaError(
                    "CENA_ADVANCEMENT_NOT_STARTED", ex.Message,
                    ErrorCategory.NotFound, null, null));
            }
        })
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
        .RequireRateLimiting("destructive")
        .WithName("OverrideAdvancementChapter");

        // ── Student self-view ────────────────────────────────────────

        var meGroup = app.MapGroup("/api/me")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        meGroup.MapGet("/advancement", async (
            string? trackId,
            ClaimsPrincipal user,
            IStudentAdvancementService svc,
            IDocumentStore store) =>
        {
            var studentId = user.FindFirst("user_id")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            // If no trackId provided, pick the first active enrollment
            if (string.IsNullOrEmpty(trackId))
            {
                await using var session = store.QuerySession();
                var enrollment = await session.Query<EnrollmentDocument>()
                    .Where(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Active)
                    .FirstOrDefaultAsync();
                trackId = enrollment?.TrackId;
            }
            if (string.IsNullOrEmpty(trackId))
                return Results.Ok(new { trackId = (string?)null, chapters = Array.Empty<object>() });

            var state = await svc.GetAsync(studentId, trackId);
            if (state is null)
                return Results.Ok(new { trackId, chapters = Array.Empty<object>(), started = false });

            // Student view hides comparative pacing delta — only "you're here"
            return Results.Ok(new
            {
                trackId = state.TrackId,
                syllabusId = state.SyllabusId,
                currentChapterId = state.CurrentChapterId,
                chapters = state.ChapterStatuses.Select(kvp => new
                {
                    chapterId = kvp.Key,
                    status = kvp.Value.ToString(),
                }).ToList(),
                started = true,
            });
        })
        .WithName("GetMyAdvancement");

        return app;
    }

    private sealed class Marker { }
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

public sealed record SyllabusResponse(
    string SyllabusId, string TrackId, string Version, string Track,
    List<string> MinistryCodes, int TotalExpectedWeeks,
    List<ChapterResponse> Chapters);

public sealed record ChapterResponse(
    string Id, int Order, string Slug,
    Dictionary<string, string> TitleByLocale,
    List<string> LearningObjectiveIds,
    List<string> PrerequisiteChapterIds,
    int ExpectedWeeks, string? MinistryCode);

public sealed record AdvancementResponse(
    string AdvancementId, string StudentId, string TrackId, string SyllabusId,
    string SyllabusVersion, string? CurrentChapterId,
    List<ChapterAdvancementResponse> Chapters,
    DateTimeOffset CreatedAt, DateTimeOffset LastAdvancedAt);

public sealed record ChapterAdvancementResponse(
    string ChapterId, string Status, DateTimeOffset LastUpdated,
    int QuestionsAttempted, float Retention);

public sealed record EnsureStartedRequest(string TrackId);

public sealed record OverrideRequest(
    string TrackId, string ChapterId, string NewStatus, string Rationale);
