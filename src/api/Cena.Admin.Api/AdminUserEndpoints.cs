// =============================================================================
// Cena Platform -- Admin User Management Endpoints
// BKD-002: Minimal API endpoint registration for user CRUD
// =============================================================================

using Cena.Admin.Api.Validation;
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
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        // GET /api/admin/users
        group.MapGet("/", async (
            string? q, string? role, string? status, string? school, string? grade,
            int? page, int? itemsPerPage, string? sortBy, string? orderBy,
            IAdminUserService service, HttpContext ctx) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(itemsPerPage);
            var result = await service.ListUsersAsync(
                q, role, status, school, grade,
                validPage, validPageSize, sortBy, orderBy, ctx.User);
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
        group.MapGet("/{id}", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            var user = await service.GetUserAsync(id, ctx.User);
            return user != null ? Results.Ok(user) : Results.NotFound();
        }).WithName("GetUser");

        // POST /api/admin/users
        group.MapPost("/", async (CreateUserRequest request, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                var user = await service.CreateUserAsync(request, ctx.User);
                return Results.Created($"/api/admin/users/{user.Id}", user);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("CreateUser");

        // PUT /api/admin/users/{id}
        group.MapPut("/{id}", async (string id, UpdateUserRequest request, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                var user = await service.UpdateUserAsync(id, request, ctx.User);
                return Results.Ok(user);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("UpdateUser");

        // DELETE /api/admin/users/{id}
        group.MapDelete("/{id}", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                await service.SoftDeleteUserAsync(id, ctx.User);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("DeleteUser");

        // POST /api/admin/users/{id}/suspend
        group.MapPost("/{id}/suspend", async (string id, SuspendUserRequest request, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                await service.SuspendUserAsync(id, request.Reason, ctx.User);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("SuspendUser");

        // POST /api/admin/users/{id}/activate
        group.MapPost("/{id}/activate", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                await service.ActivateUserAsync(id, ctx.User);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("ActivateUser");

        // POST /api/admin/users/invite
        group.MapPost("/invite", async (InviteUserRequest request, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                var user = await service.InviteUserAsync(request, ctx.User);
                return Results.Created($"/api/admin/users/{user.Id}", user);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Forbid();
            }
        }).WithName("InviteUser");

        // POST /api/admin/users/bulk-invite
        group.MapPost("/bulk-invite", async (HttpRequest request, IAdminUserService service, HttpContext ctx) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data with CSV file" });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file == null)
                return Results.BadRequest(new { error = "No file uploaded" });

            // REV-011.3: Restrict bulk-invite to CSV only
            var contentType = file.ContentType ?? "";
            if (!contentType.Equals("text/csv", StringComparison.OrdinalIgnoreCase)
                && !contentType.Equals("application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only CSV files are accepted for bulk invite" });

            const long maxBulkInviteSize = 5 * 1024 * 1024; // 5MB for CSV
            if (file.Length > maxBulkInviteSize)
                return Results.BadRequest(new { error = $"File exceeds maximum size of {maxBulkInviteSize / (1024 * 1024)}MB" });

            // Sanitize filename
            var safeName = Path.GetFileName(file.FileName)
                .Replace("..", "")
                .Replace("/", "")
                .Replace("\\", "");

            using var stream = file.OpenReadStream();
            var result = await service.BulkInviteAsync(stream, ctx.User);
            return Results.Ok(result);
        }).WithName("BulkInviteUsers").DisableAntiforgery();

        // GET /api/admin/users/{id}/security
        group.MapGet("/{id}/security", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            var user = await service.GetUserAsync(id, ctx.User);
            if (user == null)
                return Results.NotFound();
            return Results.Ok(new
            {
                twoFactorEnabled = false,
                apiKeys = Array.Empty<object>()
            });
        }).WithName("GetUserSecurity");

        // GET /api/admin/users/{id}/activity
        group.MapGet("/{id}/activity", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            var activity = await service.GetActivityAsync(id, ctx.User);
            return Results.Ok(activity);
        }).WithName("GetUserActivity");

        // GET /api/admin/users/{id}/sessions (BKD-002.9)
        group.MapGet("/{id}/sessions", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            var sessions = await service.GetSessionsAsync(id, ctx.User);
            return Results.Ok(sessions);
        }).WithName("GetUserSessions");

        // DELETE /api/admin/users/{id}/sessions/{sid} (BKD-002.9)
        group.MapDelete("/{id}/sessions/{sid}", async (string id, string sid, IAdminUserService service, HttpContext ctx) =>
        {
            try
            {
                await service.RevokeSessionAsync(id, sid, ctx.User);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("RevokeUserSession");

        // POST /api/admin/users/{id}/force-reset
        group.MapPost("/{id}/force-reset", async (string id, IAdminUserService service, HttpContext ctx) =>
        {
            var success = await service.ForcePasswordResetAsync(id, ctx.User);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("ForcePasswordReset");

        // DELETE /api/admin/users/{id}/api-keys/{keyId}
        group.MapDelete("/{id}/api-keys/{keyId}", async (string id, string keyId, IAdminUserService service, HttpContext ctx) =>
        {
            var success = await service.RevokeApiKeyAsync(id, keyId, ctx.User);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RevokeApiKey");

        return app;
    }
}
