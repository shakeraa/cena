// =============================================================================
// Tests for TenantScope.GetSchoolFilter
// Verifies claim resolution order (ClaimTypes.Role → "role" fallback),
// SUPER_ADMIN bypass, school_id extraction, and missing-claim rejection.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Tenancy;

namespace Cena.Admin.Api.Tests;

public class TenantScopeTests
{
    // ── SUPER_ADMIN returns null (no filter) ──

    [Fact]
    public void SuperAdmin_WithClaimTypesRole_ReturnsNull()
    {
        var principal = MakePrincipal(ClaimTypes.Role, "SUPER_ADMIN");

        var result = TenantScope.GetSchoolFilter(principal);

        Assert.Null(result);
    }

    [Fact]
    public void SuperAdmin_WithDirectRoleClaim_ReturnsNull()
    {
        // Firebase JWT has "role" as a direct claim before CenaClaimsTransformer runs
        var principal = MakePrincipal("role", "SUPER_ADMIN");

        var result = TenantScope.GetSchoolFilter(principal);

        Assert.Null(result);
    }

    [Fact]
    public void SuperAdmin_WithBothRoleClaims_ReturnsNull()
    {
        // After CenaClaimsTransformer: both "role" and ClaimTypes.Role exist
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "SUPER_ADMIN"),
            new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
        }, "test");

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Null(result);
    }

    // ── Non-SUPER_ADMIN with school_id returns school_id ──

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("MODERATOR")]
    public void AdminWithSchoolId_ReturnsSchoolId(string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("school_id", "school-haifa-01"),
        }, "test");

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Equal("school-haifa-01", result);
    }

    [Fact]
    public void AdminWithDirectRoleClaim_AndSchoolId_ReturnsSchoolId()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "ADMIN"),
            new Claim("school_id", "school-nazareth-01"),
        }, "test");

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Equal("school-nazareth-01", result);
    }

    // ── Non-SUPER_ADMIN without school_id throws ──

    [Fact]
    public void Admin_WithoutSchoolId_Throws()
    {
        var principal = MakePrincipal(ClaimTypes.Role, "ADMIN");

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => TenantScope.GetSchoolFilter(principal));

        Assert.Contains("school_id", ex.Message);
    }

    [Fact]
    public void Moderator_WithoutSchoolId_Throws()
    {
        var principal = MakePrincipal("role", "MODERATOR");

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => TenantScope.GetSchoolFilter(principal));

        Assert.Contains("school_id", ex.Message);
    }

    // ── Regression: "cena_role" claim must NOT be required ──

    [Fact]
    public void Regression_NoCenaRoleClaim_SuperAdmin_StillWorks()
    {
        // Before fix: code checked "cena_role" which never existed → always threw
        // After fix: checks ClaimTypes.Role and "role" — this must pass
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
        }, "test");
        // Explicitly verify no "cena_role" claim exists
        Assert.Null(identity.FindFirst("cena_role"));

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Null(result);
    }

    [Fact]
    public void Regression_NoCenaRoleClaim_AdminWithSchool_StillWorks()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("school_id", "school-haifa-01"),
        }, "test");
        Assert.Null(identity.FindFirst("cena_role"));

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Equal("school-haifa-01", result);
    }

    // ── Edge cases ──

    [Fact]
    public void NoRoleClaim_WithSchoolId_ReturnsSchoolId()
    {
        // User with no role claim but has school_id — not SUPER_ADMIN, so returns school
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("school_id", "school-test"),
        }, "test");

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Equal("school-test", result);
    }

    [Fact]
    public void NoRoleClaim_NoSchoolId_Throws()
    {
        var identity = new ClaimsIdentity([], "test");

        Assert.Throws<UnauthorizedAccessException>(
            () => TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Student_WithSchoolId_ReturnsSchoolId()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "STUDENT"),
            new Claim("school_id", "school-haifa-01"),
        }, "test");

        var result = TenantScope.GetSchoolFilter(new ClaimsPrincipal(identity));

        Assert.Equal("school-haifa-01", result);
    }

    // ── Helper ──

    private static ClaimsPrincipal MakePrincipal(string claimType, string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(claimType, role),
        }, "test");
        return new ClaimsPrincipal(identity);
    }
}
