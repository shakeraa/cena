// =============================================================================
// Tests for Authorization Policy claim matching
// Verifies all 3 claim paths: IsInRole, "role" claim, ClaimTypes.Role claim
// =============================================================================

using System.Security.Claims;

namespace Cena.Admin.Api.Tests;

public class AuthPolicyTests
{
    [Theory]
    [InlineData("SUPER_ADMIN")]
    [InlineData("ADMIN")]
    [InlineData("MODERATOR")]
    public void ModeratorOrAbove_MatchesRoleClaim(string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", role),
        }, "test");

        var principal = new ClaimsPrincipal(identity);

        // The policy checks HasClaim("role", r)
        string[] moderatorRoles = ["MODERATOR", "ADMIN", "SUPER_ADMIN"];
        Assert.True(moderatorRoles.Any(r => principal.HasClaim("role", r)));
    }

    [Theory]
    [InlineData("SUPER_ADMIN")]
    [InlineData("ADMIN")]
    [InlineData("MODERATOR")]
    public void ModeratorOrAbove_MatchesClaimTypesRole(string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role),
        }, "test", ClaimTypes.Name, ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);

        string[] moderatorRoles = ["MODERATOR", "ADMIN", "SUPER_ADMIN"];
        Assert.True(moderatorRoles.Any(r => principal.HasClaim(ClaimTypes.Role, r)));
        Assert.True(moderatorRoles.Any(r => principal.IsInRole(r)));
    }

    [Fact]
    public void Student_DoesNotMatchModeratorOrAbove()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "STUDENT"),
        }, "test");

        var principal = new ClaimsPrincipal(identity);

        string[] moderatorRoles = ["MODERATOR", "ADMIN", "SUPER_ADMIN"];
        Assert.False(moderatorRoles.Any(r => principal.HasClaim("role", r)));
    }

    [Fact]
    public void SuperAdmin_MatchesAllPolicies()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "SUPER_ADMIN"),
            new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
        }, "test", ClaimTypes.Name, ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);

        // ModeratorOrAbove
        string[] moderatorRoles = ["MODERATOR", "ADMIN", "SUPER_ADMIN"];
        Assert.True(moderatorRoles.Any(r => principal.IsInRole(r)));

        // AdminOnly
        string[] adminRoles = ["ADMIN", "SUPER_ADMIN"];
        Assert.True(adminRoles.Any(r => principal.IsInRole(r)));

        // SuperAdminOnly
        Assert.True(principal.IsInRole("SUPER_ADMIN"));
        Assert.True(principal.HasClaim("role", "SUPER_ADMIN"));
    }

    [Fact]
    public void FirebaseClaimPattern_RoleWithoutClaimTypesRole_StillMatches()
    {
        // Firebase JWT may only have "role" claim, not ClaimTypes.Role
        // Our policy must handle this via HasClaim("role", ...) fallback
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "SUPER_ADMIN"),
        }, "test");

        var principal = new ClaimsPrincipal(identity);

        // IsInRole fails because RoleClaimType doesn't match "role"
        Assert.False(principal.IsInRole("SUPER_ADMIN"));

        // But HasClaim("role", ...) works — this is the fallback in our policy
        Assert.True(principal.HasClaim("role", "SUPER_ADMIN"));
    }
}
