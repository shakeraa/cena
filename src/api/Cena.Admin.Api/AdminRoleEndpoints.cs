// =============================================================================
// Cena Platform -- Admin Roles & Permissions Endpoints
// BKD-003: Minimal API endpoint registration for role management
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class AdminRoleEndpoints
{
    public static IEndpointRouteBuilder MapAdminRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var rolesGroup = app.MapGroup("/api/v1/admin/roles")
            .WithTags("Admin Roles")
            .RequireRateLimiting("api");

        // GET /api/admin/roles — ModeratorOrAbove
        rolesGroup.MapGet("/", async (IAdminRoleService service) =>
        {
            var roles = await service.ListRolesAsync();
            return Results.Ok(roles);
        })
        .WithName("ListRoles")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // GET /api/admin/roles/{id} — ModeratorOrAbove
        rolesGroup.MapGet("/{id}", async (string id, IAdminRoleService service) =>
        {
            var role = await service.GetRoleAsync(id);
            return role != null ? Results.Ok(role) : Results.NotFound();
        })
        .WithName("GetRole")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // POST /api/admin/roles — SuperAdminOnly
        rolesGroup.MapPost("/", async (CreateRoleRequest request, IAdminRoleService service) =>
        {
            try
            {
                var role = await service.CreateRoleAsync(request);
                return Results.Created($"/api/v1/admin/roles/{role.Id}", role);
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
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status409Conflict)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

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
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

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
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
    .Produces(StatusCodes.Status204NoContent)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // GET /api/admin/permissions — ModeratorOrAbove (static data)
        app.MapGet("/api/v1/admin/permissions", async (IAdminRoleService service) =>
        {
            var permissions = await service.ListPermissionsAsync();
            return Results.Ok(permissions);
        })
        .WithTags("Admin Roles")
        .WithName("ListPermissions")
        .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // POST /api/admin/users/{id}/role — SuperAdminOnly (FIND-sec-010: privilege escalation fix)
        app.MapPost("/api/v1/admin/users/{id}/role", async (string id, AssignRoleRequest request, IAdminRoleService service, HttpContext ctx) =>
        {
            try
            {
                await service.AssignRoleToUserAsync(id, request, ctx.User);
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
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        })
        .WithTags("Admin Roles")
        .WithName("AssignRoleToUser")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
    .Produces(StatusCodes.Status204NoContent)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // GET /api/admin/users/{id}/abilities — AdminOnly
        app.MapGet("/api/v1/admin/users/{id}/abilities", async (string id, IAdminRoleService service) =>
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
        .RequireAuthorization(CenaAuthPolicies.AdminOnly)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        return app;
    }
}
