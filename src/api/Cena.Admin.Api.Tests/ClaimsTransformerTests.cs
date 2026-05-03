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

    // =========================================================================
    // prr-009: parent_of claim parsing (ADR-0041 binding cache)
    // =========================================================================

    [Fact]
    public async Task Transform_ExplodesParentOfArrayIntoPerEntryClaims()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "PARENT"),
            new Claim(
                "parent_of",
                "[{\"studentId\":\"stu-a\",\"instituteId\":\"inst-x\"},"
                + "{\"studentId\":\"stu-b\",\"instituteId\":\"inst-y\"}]"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        var entries = result.FindAll("parent_of").ToList();
        Assert.Equal(2, entries.Count);

        // Each exploded claim's value must itself be a parseable single
        // entry object — this is the shape ParentAuthorizationGuard reads.
        Assert.Contains(entries, c =>
            c.Value.Contains("\"studentId\":\"stu-a\"") &&
            c.Value.Contains("\"instituteId\":\"inst-x\""));
        Assert.Contains(entries, c =>
            c.Value.Contains("\"studentId\":\"stu-b\"") &&
            c.Value.Contains("\"instituteId\":\"inst-y\""));
    }

    [Fact]
    public async Task Transform_PreservesSingleObjectParentOfClaim()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "PARENT"),
            new Claim("parent_of", "{\"studentId\":\"stu-a\",\"instituteId\":\"inst-x\"}"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        var entries = result.FindAll("parent_of").ToList();
        Assert.Single(entries);
        Assert.Contains("stu-a", entries[0].Value);
    }

    [Fact]
    public async Task Transform_DropsMalformedParentOfClaim()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "PARENT"),
            new Claim("parent_of", "not-json{"),
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformer.TransformAsync(principal);

        // Malformed claim is dropped — downstream guard sees zero entries.
        Assert.Empty(result.FindAll("parent_of"));
    }
}
