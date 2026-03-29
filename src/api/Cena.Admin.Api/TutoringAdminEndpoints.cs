// =============================================================================
// Cena Platform -- Tutoring Admin Endpoints
// ADM-017: REST endpoints for tutoring session dashboard
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class TutoringAdminEndpoints
{
    public static IEndpointRouteBuilder MapTutoringAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/tutoring")
            .WithTags("Tutoring Admin")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/sessions", async (
            string? studentId,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            ITutoringAdminService service) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var result = await service.GetSessionsAsync(
                studentId, status, validPage, validPageSize, user);
            return Results.Ok(result);
        }).WithName("GetTutoringSessions");

        group.MapGet("/sessions/{sessionId}", async (
            string sessionId,
            ClaimsPrincipal user,
            ITutoringAdminService service) =>
        {
            var detail = await service.GetSessionDetailAsync(sessionId, user);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetTutoringSessionDetail");

        group.MapGet("/budget-status", async (
            string? classId,
            ClaimsPrincipal user,
            ITutoringAdminService service) =>
        {
            var result = await service.GetBudgetStatusAsync(classId, user);
            return Results.Ok(result);
        }).WithName("GetTutoringBudgetStatus");

        group.MapGet("/analytics", async (ClaimsPrincipal user, ITutoringAdminService service) =>
        {
            var result = await service.GetAnalyticsAsync(user);
            return Results.Ok(result);
        }).WithName("GetTutoringAnalytics");

        return app;
    }
}
