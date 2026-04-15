// =============================================================================
// Cena Platform — Rate Limit Dashboard Endpoints (RATE-001)
// Real-time spend and rate-limit status for operators.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.RateLimit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Api.Host.Endpoints;

public static class RateLimitDashboardEndpoints
{
    public static IEndpointRouteBuilder MapRateLimitDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ratelimit")
            .WithTags("Rate Limiting")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        // GET /api/ratelimit/status — current token-bucket state for the caller
        group.MapGet("/status", async (
            HttpContext ctx,
            IRateLimitService rateLimit) =>
        {
            var studentId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? ctx.User.FindFirstValue("sub")
                ?? "anonymous";

            var apiState = await rateLimit.GetBucketStateAsync(studentId, "api");
            var photoState = await rateLimit.GetBucketStateAsync(studentId, "photo");

            return Results.Ok(new
            {
                studentId,
                api = new
                {
                    remaining = apiState.RemainingTokens,
                    lastRefill = apiState.LastRefill
                },
                photo = new
                {
                    remaining = photoState.RemainingTokens,
                    lastRefill = photoState.LastRefill
                }
            });
        }).WithName("GetRateLimitStatus")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/ratelimit/spend — daily cost spend for tenant and global
        group.MapGet("/spend", async (
            HttpContext ctx,
            ICostBudgetService budget,
            ICostCircuitBreaker breaker) =>
        {
            var tenantId = ctx.User.FindFirstValue("school_id") ?? "no-school";

            var tenantUsage = await budget.GetTenantUsageAsync(tenantId);
            var globalUsage = await budget.GetGlobalUsageAsync();
            var breakerStatus = await breaker.GetStatusAsync();

            return Results.Ok(new
            {
                tenantId,
                tenant = new
                {
                    used = tenantUsage.Used,
                    limit = tenantUsage.Limit,
                    remaining = Math.Max(0, tenantUsage.Limit - tenantUsage.Used)
                },
                globalSpend = new
                {
                    used = globalUsage.Used,
                    limit = globalUsage.Limit,
                    remaining = Math.Max(0, globalUsage.Limit - globalUsage.Used)
                },
                circuitBreaker = new
                {
                    isOpen = breakerStatus.Used >= breakerStatus.Threshold,
                    dailySpend = breakerStatus.Used,
                    threshold = breakerStatus.Threshold
                }
            });
        }).WithName("GetRateLimitSpend")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
