// =============================================================================
// Cena Platform -- Stagnation Insights Endpoints
// Provides drill-down analysis for why a student is stagnating on a concept.
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class StagnationInsightsEndpoints
{
    public static IEndpointRouteBuilder MapStagnationInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/stagnation")
            .WithTags("Stagnation Insights")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /api/admin/stagnation/{studentId}/{conceptId}/insights
        // Returns causal factor analysis: difficulty, focus, prerequisites, methodology, errors
        group.MapGet("/{studentId}/{conceptId}/insights", async (
            string studentId,
            string conceptId,
            IStagnationInsightsService service) =>
        {
            var result = await service.GetInsightsAsync(studentId, conceptId);
            return Results.Ok(result);
        }).WithName("GetStagnationInsights");

        // GET /api/admin/stagnation/{studentId}/{conceptId}/timeline
        // Returns per-attempt timeline with difficulty gap, focus state, methodology
        group.MapGet("/{studentId}/{conceptId}/timeline", async (
            string studentId,
            string conceptId,
            int? limit,
            IStagnationInsightsService service) =>
        {
            var result = await service.GetTimelineAsync(studentId, conceptId, limit ?? 50);
            return Results.Ok(result);
        }).WithName("GetStagnationTimeline");

        return app;
    }
}
