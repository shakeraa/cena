// =============================================================================
// Cena Platform -- Content Moderation Endpoints
// ADM-005: Moderation queue and review API
// =============================================================================

using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class ContentModerationEndpoints
{
    public static IEndpointRouteBuilder MapContentModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/moderation")
            .WithTags("Content Moderation")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

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
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(itemsPerPage);
            var result = await service.GetQueueAsync(
                status,
                q,
                validPage,
                validPageSize,
                sortBy ?? "submittedAt",
                orderBy ?? "asc");
            return Results.Ok(result);
        }).WithName("GetModerationQueue")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/moderation/queue/summary - Queue summary stats
        group.MapGet("/queue/summary", async (IContentModerationService service) =>
        {
            var summary = await service.GetQueueSummaryAsync();
            return Results.Ok(summary);
        }).WithName("GetModerationQueueSummary")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/moderation/items/{id} - Get item detail
        group.MapGet("/items/{id}", async (string id, IContentModerationService service) =>
        {
            var item = await service.GetItemDetailAsync(id);
            return item != null ? Results.Ok(item) : Results.NotFound();
        }).WithName("GetModerationItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/moderation/items/{id}/claim - Claim item for review
        group.MapPost("/items/{id}/claim", async (
            string id,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.ClaimItemAsync(id, moderatorId);
            return success ? Results.Ok() : Results.Conflict("Item already claimed by another moderator");
        }).WithName("ClaimModerationItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status409Conflict)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/moderation/items/{id}/approve - Approve item
        group.MapPost("/items/{id}/approve", async (
            string id,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.ApproveItemAsync(id, moderatorId);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("ApproveModerationItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

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
        }).WithName("RejectModerationItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

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
        }).WithName("FlagModerationItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

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
        }).WithName("AddModerationComment")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/moderation/bulk - Bulk actions
        group.MapPost("/bulk", async (
            BulkModerationRequest request,
            HttpContext httpContext,
            IContentModerationService service) =>
        {
            var moderatorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var success = await service.BulkActionAsync(request.Action, request.ItemIds, moderatorId);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("BulkModerationAction")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/moderation/stats - Moderation statistics
        group.MapGet("/stats", async (IContentModerationService service) =>
        {
            var stats = await service.GetStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetModerationStats")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // prr-034: GET /api/admin/moderation/cultural-context-dlq
        // Community review board ops queue. Tenant-scoped via TenantScope
        // inside the service (SUPER_ADMIN sees all; others see their
        // own school only).
        group.MapGet("/cultural-context-dlq", async (
            string? status,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            ICulturalContextReviewBoardService service,
            CancellationToken ct) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var result = await service.ListAsync(
                httpContext.User,
                status,
                validPage,
                validPageSize,
                ct);
            return Results.Ok(result);
        }).WithName("GetCulturalContextDlq")
    .Produces<Cena.Api.Contracts.Admin.Cultural.CulturalContextDlqListResponse>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
