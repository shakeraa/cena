// =============================================================================
// Cena Platform -- Admin Dashboard Endpoints
// BKD-004: Minimal API endpoints for dashboard overview, activity, charts, alerts
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class AdminDashboardEndpoints
{
    public static IEndpointRouteBuilder MapAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/dashboard")
            .WithTags("Admin Dashboard")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // GET /api/admin/dashboard/home - Combined dashboard data
        group.MapGet("/home", async (IAdminDashboardService service) =>
        {
            var home = await service.GetDashboardHomeAsync();
            return Results.Ok(home);
        }).WithName("GetDashboardHome");

        // GET /api/admin/dashboard/overview
        group.MapGet("/overview", async (IAdminDashboardService service) =>
        {
            var overview = await service.GetOverviewAsync();
            return Results.Ok(overview);
        }).WithName("GetDashboardOverview");

        // GET /api/admin/dashboard/activity?period=30d
        group.MapGet("/activity", async (string? period, IAdminDashboardService service) =>
        {
            var activity = await service.GetActivityAsync(period ?? "30d");
            return Results.Ok(activity);
        }).WithName("GetDashboardActivity");

        // GET /api/admin/dashboard/content-pipeline?period=30d
        group.MapGet("/content-pipeline", async (string? period, IAdminDashboardService service) =>
        {
            var pipeline = await service.GetContentPipelineAsync(period ?? "30d");
            return Results.Ok(pipeline);
        }).WithName("GetContentPipeline");

        // GET /api/admin/dashboard/focus-distribution
        group.MapGet("/focus-distribution", async (IAdminDashboardService service) =>
        {
            var distribution = await service.GetFocusDistributionAsync();
            return Results.Ok(distribution);
        }).WithName("GetFocusDistribution");

        // GET /api/admin/dashboard/mastery-progress?period=30d
        group.MapGet("/mastery-progress", async (string? period, IAdminDashboardService service) =>
        {
            var progress = await service.GetMasteryProgressAsync(period ?? "30d");
            return Results.Ok(progress);
        }).WithName("GetMasteryProgress");

        // GET /api/admin/dashboard/alerts
        group.MapGet("/alerts", async (IAdminDashboardService service) =>
        {
            var alerts = await service.GetAlertsAsync();
            return Results.Ok(alerts);
        }).WithName("GetDashboardAlerts");

        // GET /api/admin/dashboard/recent-activity?limit=20
        group.MapGet("/recent-activity", async (int? limit, IAdminDashboardService service) =>
        {
            var activity = await service.GetRecentActivityAsync(limit ?? 20);
            return Results.Ok(activity);
        }).WithName("GetRecentAdminActivity");

        // GET /api/admin/dashboard/pending-review
        group.MapGet("/pending-review", async (IAdminDashboardService service) =>
        {
            var summary = await service.GetPendingReviewSummaryAsync();
            return Results.Ok(summary);
        }).WithName("GetPendingReviewSummary");

        return app;
    }
}
