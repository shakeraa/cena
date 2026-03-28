// =============================================================================
// Cena Platform -- Tutoring Admin Endpoints
// ADM-017: REST endpoints for tutoring session dashboard
// =============================================================================

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
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/sessions", async (
            string? studentId,
            string? status,
            int? page,
            int? pageSize,
            ITutoringAdminService service) =>
        {
            var result = await service.GetSessionsAsync(
                studentId, status, page ?? 1, pageSize ?? 20);
            return Results.Ok(result);
        }).WithName("GetTutoringSessions");

        group.MapGet("/sessions/{sessionId}", async (
            string sessionId,
            ITutoringAdminService service) =>
        {
            var detail = await service.GetSessionDetailAsync(sessionId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetTutoringSessionDetail");

        group.MapGet("/budget-status", async (
            string? classId,
            ITutoringAdminService service) =>
        {
            var result = await service.GetBudgetStatusAsync(classId);
            return Results.Ok(result);
        }).WithName("GetTutoringBudgetStatus");

        group.MapGet("/analytics", async (ITutoringAdminService service) =>
        {
            var result = await service.GetAnalyticsAsync();
            return Results.Ok(result);
        }).WithName("GetTutoringAnalytics");

        return app;
    }
}
