// =============================================================================
// Cena Platform -- Content Moderation Endpoints
// ADM-005: Moderation queue and review API
// =============================================================================

using Cena.Actors.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace Cena.Actors.Api.Admin;

public static class ContentModerationEndpoints
{
    public static IEndpointRouteBuilder MapContentModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/moderation")
            .WithTags("Content Moderation")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // GET /api/admin/moderation/queue - List moderation queue
        group.MapGet("/queue", async (
            string? status,
            string? q,
            int? page,
            int? itemsPerPage,
            string? sortBy,
            string? orderBy,
            IContentModerationService service) =>
        {
            var result = await service.GetQueueAsync(
                status,
                q,
                page ?? 1,
                itemsPerPage ?? 10,
                sortBy ?? "submittedAt",
                orderBy ?? "asc");
            return Results.Ok(result);
        }).WithName("GetModerationQueue");

        // GET /api/admin/moderation/queue/summary - Queue summary stats
        group.MapGet("/queue/summary", async (IContentModerationService service) =>
        {
            var summary = await service.GetQueueSummaryAsync();
            return Results.Ok(summary);
        }).WithName("GetModerationQueueSummary");

        // GET /api/admin/moderation/items/{id} - Get item detail
        group.MapGet("/items/{id}", async (string id, IContentModerationService service) =>
        {
            var item = await service.GetItemDetailAsync(id);
            return item != null ? Results.Ok(item) : Results.NotFound();
        }).WithName("GetModerationItem");

        // POST /api/admin/moderation/items/{id}/claim - Claim item for review
        group.MapPost("/items/{id}/claim", async (
            string id,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.ClaimItemAsync(id, moderatorId);
            return success ? Results.Ok() : Results.Conflict("Item already claimed by another moderator");
        }).WithName("ClaimModerationItem");

        // POST /api/admin/moderation/items/{id}/approve - Approve item
        group.MapPost("/items/{id}/approve", async (
            string id,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.ApproveItemAsync(id, moderatorId);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("ApproveModerationItem");

        // POST /api/admin/moderation/items/{id}/reject - Reject item
        group.MapPost("/items/{id}/reject", async (
            string id,
            RejectItemRequest request,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.RejectItemAsync(id, moderatorId, request.Reason);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RejectModerationItem");

        // POST /api/admin/moderation/items/{id}/flag - Flag item
        group.MapPost("/items/{id}/flag", async (
            string id,
            FlagItemRequest request,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.FlagItemAsync(id, moderatorId, request.Reason);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("FlagModerationItem");

        // POST /api/admin/moderation/items/{id}/comments - Add comment
        group.MapPost("/items/{id}/comments", async (
            string id,
            AddCommentRequest request,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.AddCommentAsync(id, moderatorId, request.Text);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("AddModerationComment");

        // POST /api/admin/moderation/bulk - Bulk actions
        group.MapPost("/bulk", async (
            BulkModerationRequest request,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.BulkActionAsync(request.Action, request.ItemIds, moderatorId);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("BulkModerationAction");

        // GET /api/admin/moderation/stats - Moderation statistics
        group.MapGet("/stats", async (IContentModerationService service) =>
        {
            var stats = await service.GetStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetModerationStats");

        return app;
    }
}
