// Cena Platform -- Experiment Admin Endpoints (ADM-019)

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

        group.MapGet("/", async (IExperimentAdminService service) =>
        {
            var result = await service.GetExperimentsAsync();
            return Results.Ok(result);
        }).WithName("GetExperiments");

        group.MapGet("/{experimentName}", async (string experimentName, IExperimentAdminService service) =>
        {
            var result = await service.GetExperimentDetailAsync(experimentName);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetExperimentDetail");

        group.MapGet("/{experimentName}/funnel", async (string experimentName, IExperimentAdminService service) =>
        {
            var result = await service.GetFunnelAsync(experimentName);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetExperimentFunnel");

        return app;
    }
}
