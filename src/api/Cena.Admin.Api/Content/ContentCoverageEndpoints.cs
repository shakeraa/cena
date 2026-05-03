// =============================================================================
// Cena Platform — Content Coverage Endpoints (RDY-019c / Phase 3)
//
// GET /api/v1/admin/content/coverage
//   ?minItemsPerLeaf=3
//   → ContentCoverageReport JSON
//
// Gated by ModeratorOrAbove + api rate limit, mirroring the other
// /api/admin/content/* endpoints.
// =============================================================================

using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Content;

public static class ContentCoverageEndpoints
{
    public static IEndpointRouteBuilder MapContentCoverageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/content")
            .WithTags("Content Coverage")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/coverage", async (
            int? minItemsPerLeaf,
            IContentCoverageService service,
            CancellationToken ct) =>
        {
            var report = await service.BuildReportAsync(
                minItemsPerLeaf: minItemsPerLeaf ?? 3, ct);
            return Results.Ok(report);
        })
        .WithName("GetContentCoverage")
        .Produces<ContentCoverageReport>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
