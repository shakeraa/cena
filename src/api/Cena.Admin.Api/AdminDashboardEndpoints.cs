// =============================================================================
// Cena Platform -- Admin Dashboard Endpoints
// BKD-004: Minimal API endpoints for dashboard overview, activity, charts, alerts
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class AdminDashboardEndpoints
{
    public static IEndpointRouteBuilder MapAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/dashboard")
            .WithTags("Admin Dashboard")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /api/admin/dashboard/home - Combined dashboard data
        group.MapGet("/home", async (ClaimsPrincipal user, IAdminDashboardService service) =>
        {
            var home = await service.GetDashboardHomeAsync(user);
            return Results.Ok(home);
        }).WithName("GetDashboardHome")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/overview
        group.MapGet("/overview", async (ClaimsPrincipal user, IAdminDashboardService service) =>
        {
            var overview = await service.GetOverviewAsync(user);
            return Results.Ok(overview);
        }).WithName("GetDashboardOverview")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/activity?period=30d
        group.MapGet("/activity", async (string? period, ClaimsPrincipal user, IAdminDashboardService service) =>
        {
            var validPeriod = ParameterValidator.ValidatePeriod(period);
            var activity = await service.GetActivityAsync(validPeriod, user);
            return Results.Ok(activity);
        }).WithName("GetDashboardActivity")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/content-pipeline?period=30d
        group.MapGet("/content-pipeline", async (string? period, IAdminDashboardService service) =>
        {
            var validPeriod = ParameterValidator.ValidatePeriod(period);
            var pipeline = await service.GetContentPipelineAsync(validPeriod);
            return Results.Ok(pipeline);
        }).WithName("GetContentPipeline")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/focus-distribution
        group.MapGet("/focus-distribution", async (IAdminDashboardService service) =>
        {
            var distribution = await service.GetFocusDistributionAsync();
            return Results.Ok(distribution);
        }).WithName("GetFocusDistribution")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/mastery-progress?period=30d
        group.MapGet("/mastery-progress", async (string? period, IAdminDashboardService service) =>
        {
            var validPeriod = ParameterValidator.ValidatePeriod(period);
            var progress = await service.GetMasteryProgressAsync(validPeriod);
            return Results.Ok(progress);
        }).WithName("GetMasteryProgress")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/alerts
        group.MapGet("/alerts", async (ClaimsPrincipal user, IAdminDashboardService service) =>
        {
            var alerts = await service.GetAlertsAsync(user);
            return Results.Ok(alerts);
        }).WithName("GetDashboardAlerts")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/recent-activity?limit=20
        group.MapGet("/recent-activity", async (int? limit, ClaimsPrincipal user, IAdminDashboardService service) =>
        {
            var validLimit = ParameterValidator.ValidateLimit(limit);
            var activity = await service.GetRecentActivityAsync(validLimit, user);
            return Results.Ok(activity);
        }).WithName("GetRecentAdminActivity")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/dashboard/pending-review
        group.MapGet("/pending-review", async (ClaimsPrincipal user, IAdminDashboardService service) =>
        {
            var summary = await service.GetPendingReviewSummaryAsync(user);
            return Results.Ok(summary);
        }).WithName("GetPendingReviewSummary")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
