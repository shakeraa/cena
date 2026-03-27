// =============================================================================
// Cena Platform -- Admin User Management Endpoints
// BKD-002: Minimal API endpoint registration for user CRUD
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Admin Users")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        // GET /api/admin/users
        group.MapGet("/", async (
            string? q, string? role, string? status, string? school, string? grade,
            int? page, int? itemsPerPage, string? sortBy, string? orderBy,
            IAdminUserService service, HttpContext ctx) =>
        {
            var result = await service.ListUsersAsync(
                q, role, status, school, grade,
                page ?? 1, itemsPerPage ?? 10, sortBy, orderBy, ctx.User);
            return Results.Ok(result);
        }).WithName("ListUsers");

        // GET /api/admin/users/stats (ModeratorOrAbove)
        group.MapGet("/stats", async (IAdminUserService service, HttpContext ctx) =>
        {
            var result = await service.GetStatsAsync(ctx.User);
            return Results.Ok(result);
        })
        .WithName("GetUserStats")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // GET /api/admin/users/{id}
        group.MapGet("/{id}", async (string id, IAdminUserService service) =>
        {
            var user = await service.GetUserAsync(id);
            return user != null ? Results.Ok(user) : Results.NotFound();
        }).WithName("GetUser");

        // POST /api/admin/users
        group.MapPost("/", async (CreateUserRequest request, IAdminUserService service) =>
        {
            try
            {
                var user = await service.CreateUserAsync(request);
                return Results.Created($"/api/admin/users/{user.Id}", user);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("CreateUser");

        // PUT /api/admin/users/{id}
        group.MapPut("/{id}", async (string id, UpdateUserRequest request, IAdminUserService service) =>
        {
            try
            {
                var user = await service.UpdateUserAsync(id, request);
                return Results.Ok(user);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("UpdateUser");

        // DELETE /api/admin/users/{id}
        group.MapDelete("/{id}", async (string id, IAdminUserService service) =>
        {
            try
            {
                await service.SoftDeleteUserAsync(id);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("DeleteUser");

        // POST /api/admin/users/{id}/suspend
        group.MapPost("/{id}/suspend", async (string id, SuspendUserRequest request, IAdminUserService service) =>
        {
            try
            {
                await service.SuspendUserAsync(id, request.Reason);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("SuspendUser");

        // POST /api/admin/users/{id}/activate
        group.MapPost("/{id}/activate", async (string id, IAdminUserService service) =>
        {
            try
            {
                await service.ActivateUserAsync(id);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("ActivateUser");

        // POST /api/admin/users/invite
        group.MapPost("/invite", async (InviteUserRequest request, IAdminUserService service) =>
        {
            try
            {
                var user = await service.InviteUserAsync(request);
                return Results.Created($"/api/admin/users/{user.Id}", user);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("InviteUser");

        // POST /api/admin/users/bulk-invite
        group.MapPost("/bulk-invite", async (HttpRequest request, IAdminUserService service) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data with CSV file" });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file == null)
                return Results.BadRequest(new { error = "No file uploaded" });

            using var stream = file.OpenReadStream();
            var result = await service.BulkInviteAsync(stream);
            return Results.Ok(result);
        }).WithName("BulkInviteUsers").DisableAntiforgery();

        // GET /api/admin/users/{id}/activity
        group.MapGet("/{id}/activity", async (string id, IAdminUserService service) =>
        {
            var activity = await service.GetActivityAsync(id);
            return Results.Ok(activity);
        }).WithName("GetUserActivity");

        // GET /api/admin/users/{id}/sessions (BKD-002.9)
        group.MapGet("/{id}/sessions", async (string id, IAdminUserService service) =>
        {
            var sessions = await service.GetSessionsAsync(id);
            return Results.Ok(sessions);
        }).WithName("GetUserSessions");

        // DELETE /api/admin/users/{id}/sessions/{sid} (BKD-002.9)
        group.MapDelete("/{id}/sessions/{sid}", async (string id, string sid, IAdminUserService service) =>
        {
            await service.RevokeSessionAsync(id, sid);
            return Results.NoContent();
        }).WithName("RevokeUserSession");

        return app;
    }
}
