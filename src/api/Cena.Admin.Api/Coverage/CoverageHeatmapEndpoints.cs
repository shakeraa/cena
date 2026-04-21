// =============================================================================
// Cena Platform — Admin Coverage Heatmap Endpoints (prr-209)
//
//   GET /api/admin/coverage/heatmap?track=FourUnit&institute=<id>
//       → 200 HeatmapResponse
//       → 403 ForbiddenException (tenant mismatch)
//       → 404 when `track` is not a valid TemplateTrack
//
//   GET /api/admin/coverage/heatmap/rung?topic=X&difficulty=N&methodology=M
//                                       &track=T&questionType=Q&institute=<id>
//                                       [&language=en]
//       → 200 RungDrilldownResponse
//       → 403 tenant mismatch
//       → 404 when the cell is neither declared nor tracked
//
// Auth: CenaAuthPolicies.AdminOnly (ADMIN or SUPER_ADMIN).
// Rate limit: shared "api" policy (100/min/user) — dashboards poll these.
// =============================================================================

using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Coverage;

public static class CoverageHeatmapEndpoints
{
    public const string BasePath = "/api/admin/coverage/heatmap";

    public static IEndpointRouteBuilder MapCoverageHeatmapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Coverage Heatmap")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        // GET /api/admin/coverage/heatmap?track=<track>&institute=<id>
        group.MapGet("", (
            [FromQuery] string track,
            [FromQuery] string institute,
            HttpContext http,
            [FromServices] ICoverageHeatmapService service) =>
        {
            if (string.IsNullOrWhiteSpace(track))
            {
                return Results.BadRequest(new CenaError(
                    ErrorCodes.CENA_INTERNAL_VALIDATION,
                    "track is required",
                    ErrorCategory.Validation,
                    null,
                    http.TraceIdentifier));
            }
            if (string.IsNullOrWhiteSpace(institute))
            {
                return Results.BadRequest(new CenaError(
                    ErrorCodes.CENA_INTERNAL_VALIDATION,
                    "institute is required",
                    ErrorCategory.Validation,
                    null,
                    http.TraceIdentifier));
            }

            var response = service.BuildHeatmap(track, institute, http.User);
            return response is null
                ? Results.NotFound(new CenaError(
                    ErrorCodes.CENA_INTERNAL_ERROR,
                    $"Unknown track '{track}'",
                    ErrorCategory.NotFound,
                    null,
                    http.TraceIdentifier))
                : Results.Ok(response);
        })
        .WithName("GetCoverageHeatmap")
        .Produces<CoverageHeatmapResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/coverage/heatmap/rung?topic=..&difficulty=..&methodology=..
        //                                     &track=..&questionType=..&institute=..
        //                                     [&language=en]
        group.MapGet("/rung", (
            [FromQuery] string topic,
            [FromQuery] string difficulty,
            [FromQuery] string methodology,
            [FromQuery] string track,
            [FromQuery] string questionType,
            [FromQuery] string institute,
            [FromQuery] string? language,
            HttpContext http,
            [FromServices] ICoverageHeatmapService service) =>
        {
            foreach (var (name, value) in new[]
                     {
                         ("topic", topic),
                         ("difficulty", difficulty),
                         ("methodology", methodology),
                         ("track", track),
                         ("questionType", questionType),
                         ("institute", institute),
                     })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return Results.BadRequest(new CenaError(
                        ErrorCodes.CENA_INTERNAL_VALIDATION,
                        $"{name} is required",
                        ErrorCategory.Validation,
                        null,
                        http.TraceIdentifier));
                }
            }

            var response = service.BuildRungDrilldown(
                topic: topic,
                difficulty: difficulty,
                methodology: methodology,
                track: track,
                questionType: questionType,
                language: language ?? "en",
                instituteId: institute,
                user: http.User);

            return response is null
                ? Results.NotFound(new CenaError(
                    ErrorCodes.CENA_INTERNAL_ERROR,
                    "No such coverage cell.",
                    ErrorCategory.NotFound,
                    null,
                    http.TraceIdentifier))
                : Results.Ok(response);
        })
        .WithName("GetCoverageHeatmapRung")
        .Produces<CoverageRungDrilldownResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
