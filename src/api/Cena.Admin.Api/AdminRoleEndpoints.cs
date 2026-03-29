// =============================================================================
// Cena Platform -- Admin Roles & Permissions Endpoints
// BKD-003: Minimal API endpoint registration for role management
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class AdminRoleEndpoints
{
    public static IEndpointRouteBuilder MapAdminRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var rolesGroup = app.MapGroup("/api/admin/roles")
            .WithTags("Admin Roles")
            .RequireRateLimiting("api");

        // GET /api/admin/roles — ModeratorOrAbove
        rolesGroup.MapGet("/", async (IAdminRoleService service) =>
        {
            var roles = await service.ListRolesAsync();
            return Results.Ok(roles);
        })
        .WithName("ListRoles")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // GET /api/admin/roles/{id} — ModeratorOrAbove
        rolesGroup.MapGet("/{id}", async (string id, IAdminRoleService service) =>
        {
            var role = await service.GetRoleAsync(id);
            return role != null ? Results.Ok(role) : Results.NotFound();
        })
        .WithName("GetRole")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // POST /api/admin/roles — SuperAdminOnly
        rolesGroup.MapPost("/", async (CreateRoleRequest request, IAdminRoleService service) =>
        {
            try
            {
                var role = await service.CreateRoleAsync(request);
                return Results.Created($"/api/admin/roles/{role.Id}", role);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateRole")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        // PUT /api/admin/roles/{id}/permissions — SuperAdminOnly
        rolesGroup.MapPut("/{id}/permissions", async (string id, UpdatePermissionsRequest request, IAdminRoleService service) =>
        {
            try
            {
                var role = await service.UpdatePermissionsAsync(id, request);
                return Results.Ok(role);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("UpdateRolePermissions")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        // DELETE /api/admin/roles/{id} — SuperAdminOnly
        rolesGroup.MapDelete("/{id}", async (string id, IAdminRoleService service) =>
        {
            try
            {
                await service.DeleteRoleAsync(id);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("DeleteRole")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        // GET /api/admin/permissions — ModeratorOrAbove (static data)
        app.MapGet("/api/admin/permissions", async (IAdminRoleService service) =>
        {
            var permissions = await service.ListPermissionsAsync();
            return Results.Ok(permissions);
        })
        .WithTags("Admin Roles")
        .WithName("ListPermissions")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // POST /api/admin/users/{id}/role — AdminOnly
        app.MapPost("/api/admin/users/{id}/role", async (string id, AssignRoleRequest request, IAdminRoleService service) =>
        {
            try
            {
                await service.AssignRoleToUserAsync(id, request);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithTags("Admin Roles")
        .WithName("AssignRoleToUser")
        .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        // GET /api/admin/users/{id}/abilities — AdminOnly
        app.MapGet("/api/admin/users/{id}/abilities", async (string id, IAdminRoleService service) =>
        {
            try
            {
                var abilities = await service.GetUserAbilitiesAsync(id);
                return Results.Ok(abilities);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithTags("Admin Roles")
        .WithName("GetUserAbilities")
        .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        return app;
    }
}
