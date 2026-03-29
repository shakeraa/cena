// =============================================================================
// Cena Platform -- Embedding Admin Endpoints (ADM-020)
// Corpus stats, text search, duplicate detection, reindex trigger
// =============================================================================

using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class EmbeddingAdminEndpoints
{
    public static IEndpointRouteBuilder MapEmbeddingAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/embeddings")
            .WithTags("Embedding Admin")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        // GET /api/admin/embeddings/corpus-stats
        group.MapGet("/corpus-stats", async (IEmbeddingAdminService service) =>
        {
            var stats = await service.GetCorpusStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetEmbeddingCorpusStats");

        // POST /api/admin/embeddings/search
        group.MapPost("/search", async (
            EmbeddingSearchRequest request,
            IEmbeddingAdminService service) =>
        {
            var result = await service.SearchAsync(request);
            return Results.Ok(result);
        }).WithName("SearchEmbeddings");

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
        }).WithName("GetEmbeddingDuplicates");

        // POST /api/admin/embeddings/reindex
        group.MapPost("/reindex", async (
            ReindexRequest request,
            IEmbeddingAdminService service) =>
        {
            var result = await service.RequestReindexAsync(request);
            return Results.Ok(result);
        }).WithName("RequestEmbeddingReindex");

        return app;
    }
}
