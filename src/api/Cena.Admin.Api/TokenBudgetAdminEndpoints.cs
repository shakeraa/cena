// Cena Platform -- Token Budget Admin Endpoints (ADM-023)

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class TokenBudgetAdminEndpoints
{
    public static IEndpointRouteBuilder MapTokenBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/system/token-budget")
            .WithTags("Token Budget")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        group.MapGet("/", async (
            string? classId,
            DateTimeOffset? date,
            ITokenBudgetAdminService service) =>
        {
            var result = await service.GetBudgetStatusAsync(classId, date);
            return Results.Ok(result);
        }).WithName("GetTokenBudgetStatus");

        group.MapGet("/trend", async (
            int? days,
            ITokenBudgetAdminService service) =>
        {
            var result = await service.GetTrendAsync(days ?? 7);
            return Results.Ok(result);
        }).WithName("GetTokenBudgetTrend");

        group.MapPut("/limits", async (
            UpdateBudgetLimitsRequest request,
            ITokenBudgetAdminService service) =>
        {
            var success = await service.UpdateLimitsAsync(request);
            return success
                ? Results.Ok(new { success = true, message = "Budget limits updated" })
                : Results.Problem("Failed to update budget limits");
        }).WithName("UpdateTokenBudgetLimits");

        return app;
    }
}
