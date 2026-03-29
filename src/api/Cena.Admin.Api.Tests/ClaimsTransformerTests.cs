// =============================================================================
// Tests for CenaClaimsTransformer
// Verifies Firebase JWT claims are correctly mapped to .NET ClaimTypes
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;

namespace Cena.Admin.Api.Tests;

public class ClaimsTransformerTests
{
    private readonly CenaClaimsTransformer _transformer = new();

    [Theory]
    [InlineData("SUPER_ADMIN")]
    [InlineData("ADMIN")]
    [InlineData("MODERATOR")]
    [InlineData("STUDENT")]
    public async Task Transform_MapsFirebaseRoleToClaimTypesRole(string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", role),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        Assert.NotNull(result.FindFirst(ClaimTypes.Role));
        Assert.Equal(role, result.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Transform_MapsSchoolIdClaim()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("school_id", "school-haifa-01"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        Assert.Equal("school-haifa-01", result.FindFirstValue("school_id"));
    }

    [Fact]
    public async Task Transform_DefaultsLocaleToEn()
    {
        var identity = new ClaimsIdentity([], "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        Assert.Equal("en", result.FindFirstValue("locale"));
    }

    [Fact]
    public async Task Transform_PreservesExistingLocale()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("locale", "he"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        Assert.Equal("he", result.FindFirstValue("locale"));
    }

    [Fact]
    public async Task Transform_DoesNotDuplicate_WhenClaimTypesRoleAlreadyExists()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "ADMIN"),
            new Claim(ClaimTypes.Role, "ADMIN"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        // Should not add another ClaimTypes.Role
        var roleClaims = result.FindAll(ClaimTypes.Role).ToList();
        Assert.Single(roleClaims);
    }

    [Fact]
    public async Task Transform_HandlesStudentIdsArray()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("student_ids", "[\"stu-001\",\"stu-002\"]"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        var studentIds = result.FindAll("student_id").Select(c => c.Value).ToList();
        Assert.Contains("stu-001", studentIds);
        Assert.Contains("stu-002", studentIds);
    }

    /// <summary>
    /// Integration: after Transform, TenantScope should correctly detect SUPER_ADMIN.
    /// This is the exact scenario that was broken before the fix.
    /// </summary>
    [Fact]
    public async Task Integration_TransformedClaims_WorkWithTenantScope()
    {
        // Simulate a Firebase JWT with "role": "SUPER_ADMIN" (the direct claim)
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "SUPER_ADMIN"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        // CenaClaimsTransformer maps "role" → ClaimTypes.Role
        var transformed = await _transformer.TransformAsync(principal);

        // TenantScope should now find SUPER_ADMIN via ClaimTypes.Role
        var result = Cena.Infrastructure.Tenancy.TenantScope.GetSchoolFilter(transformed);

        Assert.Null(result); // SUPER_ADMIN = no school filter
    }

    [Fact]
    public async Task Integration_TransformedAdmin_WithSchoolId_WorksWithTenantScope()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "ADMIN"),
            new Claim("school_id", "school-nazareth-01"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var transformed = await _transformer.TransformAsync(principal);

        var result = Cena.Infrastructure.Tenancy.TenantScope.GetSchoolFilter(transformed);

        Assert.Equal("school-nazareth-01", result);
    }
}
