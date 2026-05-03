// =============================================================================
// Cena Platform — CuratorMetadata Endpoints (RDY-019e-IMPL / Phase 1C)
//
// Admin-only surface for the RDY-019e curator handshake. Three routes under
// /api/admin/ingestion/pipeline/{id}/metadata:
//
//   GET    → full response (auto_extracted + current + missing_required)
//   PATCH  → partial merge of supplied fields (null = "leave alone")
//   DELETE → /{field} clears one field explicitly
//
// Auth: ModeratorOrAbove — same gate as the rest of the ingestion pipeline
// surface. Rate limiting mirrors MapIngestionPipelineEndpoints.
//
// NO STUBS. Real IDocumentStore reads + writes via CuratorMetadataService.
// =============================================================================

using Cena.Api.Contracts.Admin.Ingestion;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace Cena.Admin.Api.Ingestion;

public static class CuratorMetadataEndpoints
{
    public static IEndpointRouteBuilder MapCuratorMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion/pipeline/{id}/metadata")
            .WithTags("Ingestion CuratorMetadata")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /metadata
        group.MapGet("", async (
            string id,
            ICuratorMetadataService service,
            CancellationToken ct) =>
        {
            var response = await service.GetAsync(id, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetCuratorMetadata")
        .Produces<CuratorMetadataResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        // PATCH /metadata
        group.MapPatch("", async (
            string id,
            CuratorMetadataPatch patch,
            ClaimsPrincipal user,
            ICuratorMetadataService service,
            CancellationToken ct) =>
        {
            if (patch is null)
                return Results.BadRequest(new CenaError(
                    "invalid_body", "PATCH body is required.", ErrorCategory.Validation, null, null));

            var curatorId = user.FindFirst("user_id")?.Value
                           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? "unknown";

            var response = await service.PatchAsync(id, patch, curatorId, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("PatchCuratorMetadata")
        .Produces<CuratorMetadataResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        // DELETE /metadata/{field}
        group.MapDelete("{field}", async (
            string id,
            string field,
            ClaimsPrincipal user,
            ICuratorMetadataService service,
            CancellationToken ct) =>
        {
            var curatorId = user.FindFirst("user_id")?.Value
                           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? "unknown";
            try
            {
                var response = await service.DeleteFieldAsync(id, field, curatorId, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new CenaError(
                    "invalid_field", ex.Message, ErrorCategory.Validation, null, null));
            }
        })
        .WithName("DeleteCuratorMetadataField")
        .Produces<CuratorMetadataResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
