// =============================================================================
// Cena Platform -- Stagnation Insights Endpoints (Job-Based)
// POST to submit → GET to poll. Rate-limited, deduped, cached.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Ingest;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class StagnationInsightsEndpoints
{
    public static IEndpointRouteBuilder MapStagnationInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/stagnation")
            .WithTags("Stagnation Insights")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // POST /api/admin/stagnation/analyze — submit an analysis job
        group.MapPost("/analyze", async (
            AnalyzeRequest request,
            HttpContext http,
            IStagnationInsightsService service) =>
        {
            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var jobType = request.Type?.ToLowerInvariant() switch
            {
                "timeline" => AnalysisJobType.StagnationTimeline,
                _ => AnalysisJobType.StagnationInsights
            };

            var result = await service.SubmitAsync(jobType, request.StudentId, request.ConceptId, userId);

            return result.Status switch
            {
                "rate_limited" => Results.StatusCode(429),
                "cached" => Results.Ok(result),
                _ => Results.Accepted($"/api/v1/admin/stagnation/jobs/{result.JobId}", result)
            };
        }).WithName("SubmitStagnationAnalysis")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/stagnation/jobs/{jobId} — poll job status + result
        group.MapGet("/jobs/{jobId}", async (
            string jobId,
            IStagnationInsightsService service) =>
        {
            var result = await service.PollAsync(jobId);
            return result.Status switch
            {
                "not_found" => Results.NotFound(result),
                "completed" => Results.Ok(result),
                "failed" => Results.Ok(result),
                _ => Results.Ok(result) // queued or processing
            };
        }).WithName("PollStagnationJob")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}

public sealed record AnalyzeRequest(
    string StudentId,
    string ConceptId,
    string? Type = "insights");  // "insights" or "timeline"
