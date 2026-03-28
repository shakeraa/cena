// =============================================================================
// Cena Platform -- Explanation Cache Admin Endpoints (ADM-018)
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class ExplanationCacheAdminEndpoints
{
    public static IEndpointRouteBuilder MapExplanationCacheEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/explanations")
            .WithTags("Explanation Cache Admin")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        // GET /api/admin/explanations/cache-stats
        group.MapGet("/cache-stats", async (IExplanationCacheAdminService service) =>
        {
            var stats = await service.GetCacheStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetExplanationCacheStats");

        // GET /api/admin/explanations/by-question/{questionId}
        group.MapGet("/by-question/{questionId}", async (
            string questionId,
            IExplanationCacheAdminService service) =>
        {
            var result = await service.GetExplanationsByQuestionAsync(questionId);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetExplanationsByQuestion");

        // POST /api/admin/explanations/invalidate
        group.MapPost("/invalidate", async (
            InvalidateCacheRequest request,
            IExplanationCacheAdminService service) =>
        {
            var result = await service.InvalidateCacheAsync(request);
            return Results.Ok(result);
        }).WithName("InvalidateExplanationCache");

        // GET /api/admin/explanations/quality-scores
        group.MapGet("/quality-scores", async (
            float? minScore,
            float? maxScore,
            int? page,
            int? pageSize,
            IExplanationCacheAdminService service) =>
        {
            var result = await service.GetQualityScoresAsync(
                minScore ?? 0f,
                maxScore ?? 1f,
                page ?? 1,
                pageSize ?? 20);
            return Results.Ok(result);
        }).WithName("GetExplanationQualityScores");

        return app;
    }
}
