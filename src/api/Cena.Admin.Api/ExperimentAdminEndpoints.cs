// Cena Platform -- Experiment Admin Endpoints (FIND-data-026)
// Tenant-scoped experiment analytics with optimized queries.

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class ExperimentAdminEndpoints
{
    public static IEndpointRouteBuilder MapExperimentAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/experiments")
            .WithTags("Experiment Admin")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // FIND-data-026: Pass ClaimsPrincipal for tenant scoping
        group.MapGet("/", async (ClaimsPrincipal user, IExperimentAdminService service) =>
        {
            var result = await service.GetExperimentsAsync(user);
            return Results.Ok(result);
        }).WithName("GetExperiments");

        group.MapGet("/{experimentName}", async (string experimentName, ClaimsPrincipal user, IExperimentAdminService service) =>
        {
            var result = await service.GetExperimentDetailAsync(experimentName, user);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetExperimentDetail");

        group.MapGet("/{experimentName}/funnel", async (string experimentName, ClaimsPrincipal user, IExperimentAdminService service) =>
        {
            var result = await service.GetFunnelAsync(experimentName, user);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetExperimentFunnel");

        return app;
    }
}
