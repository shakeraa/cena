// =============================================================================
// Cena Platform -- Authorization Policies
// BKD-001.3: Role-based + org-scoped policies for admin API
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Infrastructure.Auth;

public static class CenaAuthPolicies
{
    public const string ModeratorOrAbove = nameof(ModeratorOrAbove);
    public const string AdminOnly = nameof(AdminOnly);
    public const string SuperAdminOnly = nameof(SuperAdminOnly);
    public const string SameOrg = nameof(SameOrg);

    private static readonly string[] ModeratorRoles = ["MODERATOR", "ADMIN", "SUPER_ADMIN"];
    private static readonly string[] AdminRoles = ["ADMIN", "SUPER_ADMIN"];

    /// <summary>
    /// Registers all Cena authorization policies.
    /// </summary>
    public static IServiceCollection AddCenaAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ModeratorOrAbove, policy =>
                policy.RequireAssertion(ctx =>
                    ModeratorRoles.Any(r => ctx.User.IsInRole(r))
                    || ModeratorRoles.Any(r => ctx.User.HasClaim("role", r))
                    || ModeratorRoles.Any(r => ctx.User.HasClaim(ClaimTypes.Role, r))));

            options.AddPolicy(AdminOnly, policy =>
                policy.RequireAssertion(ctx =>
                    AdminRoles.Any(r => ctx.User.IsInRole(r))
                    || AdminRoles.Any(r => ctx.User.HasClaim("role", r))
                    || AdminRoles.Any(r => ctx.User.HasClaim(ClaimTypes.Role, r))));

            options.AddPolicy(SuperAdminOnly, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole("SUPER_ADMIN")
                    || ctx.User.HasClaim("role", "SUPER_ADMIN")
                    || ctx.User.HasClaim(ClaimTypes.Role, "SUPER_ADMIN")));

            options.AddPolicy(SameOrg, policy =>
                policy.AddRequirements(new SameOrgRequirement()));
        });

        services.AddSingleton<IAuthorizationHandler, SameOrgHandler>();

        return services;
    }
}

/// <summary>
/// Requires the user's school_id claim to match the route {orgId} parameter,
/// or the user to be SUPER_ADMIN (sees all orgs).
/// </summary>
public sealed class SameOrgRequirement : IAuthorizationRequirement;

public sealed class SameOrgHandler : AuthorizationHandler<SameOrgRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SameOrgHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SameOrgRequirement requirement)
    {
        if (context.User.IsInRole("SUPER_ADMIN"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var routeOrgId = httpContext?.GetRouteValue("orgId")?.ToString();
        var userSchoolId = context.User.FindFirstValue("school_id");

        if (routeOrgId != null && userSchoolId != null &&
            string.Equals(routeOrgId, userSchoolId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
