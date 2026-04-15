// =============================================================================
// Cena Platform — Rate Limit Admin Endpoints (RATE-001 Dashboard)
// =============================================================================

using Cena.Admin.Api.RateLimit;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class RateLimitAdminEndpoints
{
    public static IEndpointRouteBuilder MapRateLimitAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/system/rate-limits")
            .WithTags("Rate Limits")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        group.MapGet("/dashboard", async (IRateLimitAdminService service) =>
        {
            var dashboard = await service.GetDashboardAsync();
            return Results.Ok(dashboard);
        }).WithName("GetRateLimitDashboard");

        group.MapPut("/tenant-budget/{tenantId}", async (
            string tenantId,
            double limitUsd,
            IRateLimitAdminService service) =>
        {
            var success = await service.UpdateTenantBudgetAsync(tenantId, limitUsd);
            return success
                ? Results.Ok(new { success = true, tenantId, limitUsd })
                : Results.Problem("Failed to update tenant budget");
        }).WithName("UpdateTenantRateLimitBudget");

        group.MapPut("/global-budget", async (
            double limitUsd,
            IRateLimitAdminService service) =>
        {
            var success = await service.UpdateGlobalBudgetAsync(limitUsd);
            return success
                ? Results.Ok(new { success = true, limitUsd })
                : Results.Problem("Failed to update global budget");
        }).WithName("UpdateGlobalRateLimitBudget");

        group.MapPost("/circuit-breaker/reset", async (IRateLimitAdminService service) =>
        {
            var success = await service.ResetCircuitBreakerAsync();
            return success
                ? Results.Ok(new { success = true, message = "Circuit breaker reset requested" })
                : Results.Problem("Failed to reset circuit breaker");
        }).WithName("ResetCostCircuitBreaker");

        return app;
    }
}
