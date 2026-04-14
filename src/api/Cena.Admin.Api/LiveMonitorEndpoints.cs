// =============================================================================
// Cena Platform -- Live Monitor Endpoints
// ADM-026: SSE stream + REST snapshot for live session monitor page
// =============================================================================

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class LiveMonitorEndpoints
{
    public static IEndpointRouteBuilder MapLiveMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/live")
            .WithTags("Live Monitor")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);
        // No rate-limiting on SSE — it is a long-lived streaming connection

        // ── GET /api/admin/live/sessions ── SSE stream for all teacher's students ──
        group.MapGet("/sessions", async (
            HttpContext http,
            ClaimsPrincipal user,
            ILiveMonitorService service) =>
        {
            var lastEventId = http.Request.Headers["Last-Event-ID"].FirstOrDefault();
            await WriteSseStreamAsync(http, user, null, lastEventId, service);
        })
        .WithName("LiveSessionsStream")
        .Produces(200, contentType: "text/event-stream");

        // ── GET /api/admin/live/sessions/{studentId} ── SSE stream for one student ──
        group.MapGet("/sessions/{studentId}", async (
            string studentId,
            HttpContext http,
            ClaimsPrincipal user,
            ILiveMonitorService service) =>
        {
            var lastEventId = http.Request.Headers["Last-Event-ID"].FirstOrDefault();
            await WriteSseStreamAsync(http, user, studentId, lastEventId, service);
        })
        .WithName("LiveStudentSessionStream")
        .Produces(200, contentType: "text/event-stream");

        // ── GET /api/admin/live/sessions/snapshot ── REST snapshot for initial page load ──
        group.MapGet("/sessions/snapshot", async (
            ClaimsPrincipal user,
            ILiveMonitorService service) =>
        {
            var result = await service.GetActiveSessionsAsync(user);
            return Results.Ok(result);
        })
        .WithName("LiveSessionsSnapshot")
        .RequireRateLimiting("api")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task WriteSseStreamAsync(
        HttpContext http,
        ClaimsPrincipal user,
        string? filterStudentId,
        string? lastEventId,
        ILiveMonitorService service)
    {
        http.Response.Headers["Content-Type"]  = "text/event-stream";
        http.Response.Headers["Cache-Control"] = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no"; // Nginx: disable buffering

        var ct = http.RequestAborted;

        try
        {
            await foreach (var ev in service.StreamAsync(user, filterStudentId, lastEventId, ct))
            {
                // SSE format: id: \nevent: \ndata: \n\n
                var sseFrame = $"id: {ev.Id}\nevent: {ev.Event}\ndata: {ev.PayloadJson}\n\n";
                var bytes = Encoding.UTF8.GetBytes(sseFrame);
                await http.Response.Body.WriteAsync(bytes, ct);
                await http.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal
        }
    }
}
