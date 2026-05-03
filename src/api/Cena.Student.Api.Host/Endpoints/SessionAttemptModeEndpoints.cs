// =============================================================================
// Cena Platform — Session attempt-mode endpoints (EPIC-PRR-F PRR-260)
//
// Two routes that let a student read + flip the hide-then-reveal toggle
// for their current learning session:
//
//   GET   /api/sessions/{sessionId}/attempt-mode
//   PATCH /api/sessions/{sessionId}/attempt-mode   body: {"mode":"visible"|"hidden_reveal"}
//
// Auth + ownership discipline (mirrors HintLadderEndpoint.cs patterns):
//   1. Authenticated student only; sub claim required.
//   2. Load LearningSessionQueueProjection; 404 if the session is unknown.
//   3. Tenant-scope via ResourceOwnershipGuard: student subject on the
//      JWT must match the session's StudentId — same IDOR guard used by
//      /hint/next. Cross-student writes are rejected with 403.
//
// Session-scope only. PRR-260 explicitly rejects a cross-session default:
// the student re-opts-in each session to preserve autonomy
// (persona-ethics guardrail). A student who wants hidden_reveal every
// session flips the toggle at each session start; there is no "persist
// this preference to my profile" affordance here.
//
// Precondition rule (mirrors hint-ladder): the toggle write only lands
// while the session is still open (EndedAt == null). After the session
// ends the projection is immutable — mode lookups succeed but writes 409.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Projections;
using Cena.Actors.Sessions;
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

/// <summary>
/// Student-scoped attempt-mode reads and writes for the hide-then-reveal
/// toggle. Mounts under the existing <c>/api/sessions</c> group to share
/// auth conventions with the hint-ladder endpoint.
/// </summary>
public static class SessionAttemptModeEndpoints
{
    private sealed class SessionAttemptModeLogMarker { }

    /// <summary>Register <c>GET</c> + <c>PATCH /api/sessions/{id}/attempt-mode</c>.</summary>
    public static IEndpointRouteBuilder MapSessionAttemptModeEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions")
            .WithTags("Sessions")
            .RequireAuthorization();

        group.MapGet("/{sessionId}/attempt-mode", GetAsync)
            .WithName("GetSessionAttemptMode")
            .Produces<SessionAttemptModeResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        group.MapPatch("/{sessionId}/attempt-mode", PatchAsync)
            .WithName("SetSessionAttemptMode")
            .Produces<SessionAttemptModeResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        return app;
    }

    // ----- GET attempt-mode --------------------------------------------------

    private static async Task<IResult> GetAsync(
        string sessionId,
        HttpContext http,
        IDocumentStore store,
        ILogger<SessionAttemptModeLogMarker> logger)
    {
        var studentId = GetStudentId(http.User);
        if (string.IsNullOrWhiteSpace(studentId)) return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(http.User, studentId);

        await using var session = store.LightweightSession();
        var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
        if (queue is null) return Results.NotFound(new { error = "session_not_found" });

        if (queue.StudentId != studentId)
        {
            logger.LogWarning(
                "[SIEM] SessionAttemptModeGet: ownership mismatch. caller={CallerPrefix} sessionOwner={OwnerPrefix} sessionId={SessionId}",
                studentId.Length <= 8 ? studentId : studentId[..8] + "…",
                queue.StudentId.Length <= 8 ? queue.StudentId : queue.StudentId[..8] + "…",
                sessionId);
            return Results.Forbid();
        }

        return Results.Ok(new SessionAttemptModeResponseDto(
            SessionId: sessionId,
            Mode: CanonicaliseWire(queue.AttemptMode)));
    }

    // ----- PATCH attempt-mode -----------------------------------------------

    private static async Task<IResult> PatchAsync(
        string sessionId,
        [FromBody] SessionAttemptModeUpdateRequestDto body,
        HttpContext http,
        IDocumentStore store,
        ILogger<SessionAttemptModeLogMarker> logger)
    {
        var studentId = GetStudentId(http.User);
        if (string.IsNullOrWhiteSpace(studentId)) return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(http.User, studentId);

        if (body is null)
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }
        if (!SessionAttemptModeWire.TryParse(body.Mode, out var mode))
        {
            return Results.BadRequest(new
            {
                error = "invalid_mode",
                accepted = new[] { SessionAttemptModeWire.Visible, SessionAttemptModeWire.HiddenReveal },
            });
        }

        await using var session = store.LightweightSession();
        var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
        if (queue is null) return Results.NotFound(new { error = "session_not_found" });
        if (queue.StudentId != studentId) return Results.Forbid();

        // Precondition: writing to a terminated session is a 409. The
        // toggle is session-scoped — once the session ends the mode is
        // frozen for audit / replay.
        if (queue.EndedAt is not null)
        {
            return Results.Conflict(new { error = "session_ended" });
        }

        var newWire = SessionAttemptModeWire.ToWire(mode);
        // Idempotent — a no-op write (same mode) still succeeds 200; the
        // client may be settling UI state after a reload.
        if (queue.AttemptMode != newWire)
        {
            queue.AttemptMode = newWire;
            session.Store(queue);
            await session.SaveChangesAsync();
            logger.LogInformation(
                "[SIEM] SessionAttemptModeSet: student={StudentPrefix} sessionId={SessionId} mode={Mode}",
                studentId.Length <= 8 ? studentId : studentId[..8] + "…",
                sessionId,
                newWire);
        }

        return Results.Ok(new SessionAttemptModeResponseDto(
            SessionId: sessionId,
            Mode: newWire));
    }

    // ----- Helpers -----------------------------------------------------------

    /// <summary>
    /// Canonicalise a stored wire value. Forward-compat with older projections
    /// that predate the field: an unknown / empty value reads as the Visible
    /// default, never as a wire error. Unknown values also round-trip through
    /// this method when surfacing on GET so the client never sees garbage.
    /// </summary>
    private static string CanonicaliseWire(string? stored)
    {
        if (SessionAttemptModeWire.TryParse(stored, out var mode))
        {
            return SessionAttemptModeWire.ToWire(mode);
        }
        return SessionAttemptModeWire.Visible;
    }

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");
}
