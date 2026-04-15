// =============================================================================
// Cena Platform -- Embedding Admin Endpoints (ADM-020)
// Corpus stats, text search, duplicate detection, reindex trigger
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class EmbeddingAdminEndpoints
{
    public static IEndpointRouteBuilder MapEmbeddingAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/embeddings")
            .WithTags("Embedding Admin")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        // GET /api/admin/embeddings/corpus-stats
        group.MapGet("/corpus-stats", async (IEmbeddingAdminService service) =>
        {
            var stats = await service.GetCorpusStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetEmbeddingCorpusStats")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/embeddings/search
        group.MapPost("/search", async (
            EmbeddingSearchRequest request,
            IEmbeddingAdminService service) =>
        {
            var result = await service.SearchAsync(request);
            return Results.Ok(result);
        }).WithName("SearchEmbeddings")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/embeddings/duplicates?threshold=0.95&page=1&pageSize=20
        group.MapGet("/duplicates", async (
            float? threshold,
            int? page,
            int? pageSize,
            IEmbeddingAdminService service) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var result = await service.GetDuplicatesAsync(
                threshold ?? 0.95f,
                validPage,
                validPageSize);
            return Results.Ok(result);
        }).WithName("GetEmbeddingDuplicates")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/embeddings/reindex
        group.MapPost("/reindex", async (
            ReindexRequest request,
            HttpContext ctx,
            IEmbeddingAdminService service) =>
        {
            var userId = ctx.User.FindFirstValue("user_id")
                ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? ctx.User.FindFirstValue("sub")
                ?? "unknown";

            var result = await service.RequestReindexAsync(request, userId);
            return Results.Ok(result);
        }).WithName("RequestEmbeddingReindex")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
