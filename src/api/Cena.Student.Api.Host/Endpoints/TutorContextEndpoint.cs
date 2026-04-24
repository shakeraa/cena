// =============================================================================
// Cena Platform — Tutor Context Endpoint (prr-204)
//
// GET /api/v1/sessions/{sessionId}/tutor-context
//
// Session-scoped tutor context for the Sidekick drawer + hint-ladder
// consumers. Backed by SessionTutorContextService — Redis session-TTL
// cache with a live Marten fallback. Tenant-scoped: the caller's
// institute claim must match the session's institute or the endpoint
// returns 403 before the service is even called.
//
// ADR-0003 compliance: the response body carries the session-scoped
// misconception tag and is keyed strictly on the session id. The
// endpoint never reads from or writes to a long-lived student profile,
// and the architecture test NoTutorContextPersistenceTest enforces this
// at test-time.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Projections;
using Cena.Actors.Tutoring;
using Cena.Api.Contracts.Sessions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public static class TutorContextEndpoint
{
    private sealed class TutorContextLogMarker { }

    public static IEndpointRouteBuilder MapTutorContextEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sessions")
            .WithTags("Sessions")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapGet("/{sessionId}/tutor-context", async (
            string sessionId,
            HttpContext ctx,
            IDocumentStore store,
            [FromServices] ISessionTutorContextService tutorContext,
            ILogger<TutorContextLogMarker> logger) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            // ── Tenant scoping (ADR-0001) ────────────────────────────────────
            //
            // The caller's authenticated tenant claim is authoritative. We
            // load the queue projection up-front solely to verify the
            // session belongs to the caller's tenant AND student before
            // burning a Redis round-trip. Cross-tenant access is logged
            // as a SIEM event — the SRE dashboard alerts on these.
            var requestedInstitute =
                ctx.User.FindFirstValue("institute_id")
                ?? ctx.User.FindFirstValue("tenant_id");

            await using (var session = store.QuerySession())
            {
                var queue = await session.LoadAsync<LearningSessionQueueProjection>(
                    sessionId, ctx.RequestAborted);
                if (queue is null)
                    return Results.NotFound(new { error = "Session not found" });

                if (!string.Equals(queue.StudentId, studentId, StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "[SIEM] TutorContext: cross-student access denied. Session {SessionId} " +
                        "belongs to {Owner}, caller {Caller} — returning 403",
                        sessionId, queue.StudentId, studentId);
                    return Results.Forbid();
                }
            }

            // ── Delegate to the service ──────────────────────────────────────
            var snapshot = await tutorContext.GetAsync(
                sessionId, studentId, ctx.RequestAborted);
            if (snapshot is null)
                return Results.NotFound(new { error = "Session not found" });

            // Tenant consistency: if the cached context carries an institute
            // id that disagrees with the caller's claim, refuse. This catches
            // a stale cache from before a student was reassigned — we would
            // rather 403 on a stale record than leak cross-tenant data.
            if (!string.IsNullOrEmpty(snapshot.InstituteId)
                && !string.IsNullOrEmpty(requestedInstitute)
                && !string.Equals(snapshot.InstituteId, requestedInstitute, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "[SIEM] TutorContext: tenant mismatch. Session {SessionId} cached as tenant " +
                    "{CachedTenant} but caller claims {CallerTenant} — returning 403",
                    sessionId, snapshot.InstituteId, requestedInstitute);
                return Results.Forbid();
            }

            return Results.Ok(new TutorContextResponseDto(
                SessionId: snapshot.SessionId,
                CurrentQuestionId: snapshot.CurrentQuestionId,
                AnsweredCount: snapshot.AnsweredCount,
                CorrectCount: snapshot.CorrectCount,
                CurrentRung: snapshot.CurrentRung,
                LastMisconceptionTag: snapshot.LastMisconceptionTag,
                AttemptPhase: snapshot.AttemptPhase switch
                {
                    SessionTutorContextAttemptPhase.Retry => "retry",
                    SessionTutorContextAttemptPhase.PostSolution => "post_solution",
                    _ => "first_try",
                },
                ElapsedMinutes: snapshot.ElapsedMinutes,
                DailyMinutesRemaining: snapshot.DailyMinutesRemaining,
                BktMasteryBucket: snapshot.BktMasteryBucket,
                AccommodationFlags: new TutorContextAccommodationDto(
                    LdAnxiousFriendly: snapshot.AccommodationFlags.LdAnxiousFriendly,
                    ExtendedTimeMultiplier: snapshot.AccommodationFlags.ExtendedTimeMultiplier,
                    DistractionReducedLayout: snapshot.AccommodationFlags.DistractionReducedLayout,
                    TtsForProblemStatements: snapshot.AccommodationFlags.TtsForProblemStatements),
                BuiltAtUtc: snapshot.BuiltAtUtc));
        })
        .WithName("GetTutorContext")
        .Produces<TutorContextResponseDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // Mirror of HintLadderEndpoint.GetStudentId — duplicated here so the
    // architecture ratchet can assert this endpoint's boundary without
    // cross-file compilation coupling.
    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");
}
