// =============================================================================
// Cena Platform — Peer-Confused Signal Endpoints (prr-159 / F5)
//
// Student-facing endpoints for the anonymous "I'm confused too" signal.
//
//   POST /api/sessions/{sid}/question/{qid}/peer-confused-signal
//        — emit the signal. Idempotent per emitter. 202 on Recorded /
//          AlreadyEmitted, 409 on tenant mismatch.
//
//   GET  /api/sessions/{sid}/question/{qid}/peer-confused-signal
//        — read the visible count. Count is null and
//          BelowAnonymityFloor=true below the k-anonymity floor (default 3).
//
// The emitting student's identity is never returned in the response.
//
// TENANCY: endpoint extracts the caller's school_id claim via TenantScope
// and passes it to the service, which first-writer-wins records the
// tenant and rejects later cross-tenant emissions.
//
// RATE LIMITING: uses the standard "api" rate-limit policy plus the
// service's per-emitter idempotency, so repeated button presses do not
// amplify the count.
// =============================================================================

using System.Security.Claims;
using System.Threading;
using Cena.Actors.Sessions;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

/// <summary>Emit response body. No identities.</summary>
public sealed record PeerConfusedEmitResponse(
    string SessionId,
    string QuestionId,
    string Outcome);        // "recorded" | "already_emitted"

/// <summary>Visible-count response body. No identities.</summary>
public sealed record PeerConfusedCountResponse(
    string SessionId,
    string QuestionId,
    int? Count,
    bool BelowAnonymityFloor,
    int AnonymityFloor);

public static class PeerConfusedSignalEndpoints
{
    private sealed class LogMarker { }

    public static IEndpointRouteBuilder MapPeerConfusedSignalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/sessions/{sessionId}/question/{questionId}/peer-confused-signal")
            .WithTags("PeerConfusedSignal")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapPost("", async (
                string sessionId,
                string questionId,
                HttpContext ctx,
                IPeerConfusedSignalService service,
                ILogger<LogMarker> logger,
                CancellationToken ct) =>
            {
                var studentId = GetStudentId(ctx.User);
                if (string.IsNullOrEmpty(studentId))
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(questionId))
                {
                    return Results.Json(
                        new CenaError(
                            "invalid_route",
                            "sessionId and questionId are required.",
                            ErrorCategory.Validation, null, null),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                string? schoolId;
                try
                {
                    schoolId = TenantScope.GetSchoolFilter(ctx.User);
                }
                catch (UnauthorizedAccessException)
                {
                    // Students must have a school claim; reject cleanly.
                    return Results.Unauthorized();
                }

                var outcome = await service.EmitAsync(studentId, sessionId, questionId, schoolId, ct);
                return outcome switch
                {
                    PeerConfusedEmitOutcome.Recorded =>
                        Results.Accepted(
                            value: new PeerConfusedEmitResponse(sessionId, questionId, "recorded")),
                    PeerConfusedEmitOutcome.AlreadyEmitted =>
                        Results.Accepted(
                            value: new PeerConfusedEmitResponse(sessionId, questionId, "already_emitted")),
                    PeerConfusedEmitOutcome.TenantMismatch =>
                        Results.Conflict(new CenaError(
                            "tenant_mismatch",
                            "This session belongs to a different school.",
                            ErrorCategory.Validation, null, null)),
                    _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
                };
            })
            .WithName("EmitPeerConfusedSignal")
            .Produces<PeerConfusedEmitResponse>(StatusCodes.Status202Accepted)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapGet("", async (
                string sessionId,
                string questionId,
                HttpContext ctx,
                IPeerConfusedSignalService service,
                CancellationToken ct) =>
            {
                if (GetStudentId(ctx.User) is null)
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(questionId))
                {
                    return Results.Json(
                        new CenaError(
                            "invalid_route",
                            "sessionId and questionId are required.",
                            ErrorCategory.Validation, null, null),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var visible = await service.GetVisibleCountAsync(sessionId, questionId, ct);
                return Results.Ok(new PeerConfusedCountResponse(
                    SessionId: visible.SessionId,
                    QuestionId: visible.QuestionId,
                    Count: visible.Count,
                    BelowAnonymityFloor: visible.BelowAnonymityFloor,
                    AnonymityFloor: visible.AnonymityFloor));
            })
            .WithName("GetPeerConfusedSignal")
            .Produces<PeerConfusedCountResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? user.FindFirstValue("user_id");
}
