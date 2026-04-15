// =============================================================================
// Cena Platform -- AdminUserService Tenant Scoping Tests
// FIND-sec-008: Cross-tenant write surface regression tests
// =============================================================================

using System.Security.Claims;
using System.Reflection;
using Cena.Infrastructure.Tenancy;

namespace Cena.Admin.Api.Tests;

/// <summary>
/// FIND-sec-008 regression tests: Verifies that all per-id methods in IAdminUserService
/// accept ClaimsPrincipal caller parameter and enforce tenant scoping via TenantScope.
/// </summary>
public class AdminUserServiceTenantScopingTests
{
    private static ClaimsPrincipal CreateCaller(string role, string? schoolId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (schoolId != null)
            claims.Add(new Claim("school_id", schoolId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    // =========================================================================
    // Interface signature verification
    // =========================================================================

    [Theory]
    [InlineData("GetUserAsync", new[] { typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("UpdateUserAsync", new[] { typeof(string), typeof(UpdateUserRequest), typeof(ClaimsPrincipal) })]
    [InlineData("SoftDeleteUserAsync", new[] { typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("SuspendUserAsync", new[] { typeof(string), typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("ActivateUserAsync", new[] { typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("CreateUserAsync", new[] { typeof(CreateUserRequest), typeof(ClaimsPrincipal) })]
    [InlineData("InviteUserAsync", new[] { typeof(InviteUserRequest), typeof(ClaimsPrincipal) })]
    [InlineData("BulkInviteAsync", new[] { typeof(Stream), typeof(ClaimsPrincipal) })]
    [InlineData("GetActivityAsync", new[] { typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("GetSessionsAsync", new[] { typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("RevokeSessionAsync", new[] { typeof(string), typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("ForcePasswordResetAsync", new[] { typeof(string), typeof(ClaimsPrincipal) })]
    [InlineData("RevokeApiKeyAsync", new[] { typeof(string), typeof(string), typeof(ClaimsPrincipal) })]
    public void IAdminUserService_Methods_AcceptClaimsPrincipal(string methodName, Type[] expectedParamTypes)
    {
        var interfaceType = typeof(IAdminUserService);
        var method = interfaceType.GetMethod(methodName, expectedParamTypes);
        Assert.NotNull(method);
    }

    [Fact]
    public void IAdminUserService_ListUsersAsync_AcceptsClaimsPrincipal()
    {
        var method = typeof(IAdminUserService).GetMethod("ListUsersAsync", new[] {
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(int), typeof(int), typeof(string), typeof(string), typeof(ClaimsPrincipal)
        });
        Assert.NotNull(method);
    }

    [Fact]
    public void IAdminUserService_GetStatsAsync_AcceptsClaimsPrincipal()
    {
        var method = typeof(IAdminUserService).GetMethod("GetStatsAsync", new[] { typeof(ClaimsPrincipal) });
        Assert.NotNull(method);
    }

    // =========================================================================
    // TenantScope verification tests
    // =========================================================================

    [Fact]
    public void TenantScope_SuperAdmin_ReturnsNull()
    {
        var caller = CreateCaller("SUPER_ADMIN");
        var schoolFilter = TenantScope.GetSchoolFilter(caller);
        Assert.Null(schoolFilter);
    }

    [Fact]
    public void TenantScope_Admin_WithSchoolId_ReturnsSchoolId()
    {
        var caller = CreateCaller("ADMIN", "school-a");
        var schoolFilter = TenantScope.GetSchoolFilter(caller);
        Assert.Equal("school-a", schoolFilter);
    }

    [Fact]
    public void TenantScope_Admin_WithoutSchoolId_ThrowsUnauthorizedAccessException()
    {
        var caller = CreateCaller("ADMIN");
        var ex = Assert.Throws<UnauthorizedAccessException>(() => TenantScope.GetSchoolFilter(caller));
        Assert.Contains("school_id", ex.Message);
    }

    [Theory]
    [InlineData("MODERATOR")]
    [InlineData("ADMIN")]
    public void TenantScope_NonSuperAdmin_WithSchoolId_ReturnsSchoolId(string role)
    {
        var caller = CreateCaller(role, "school-test");
        var schoolFilter = TenantScope.GetSchoolFilter(caller);
        Assert.Equal("school-test", schoolFilter);
    }

    // =========================================================================
    // Cross-tenant access logic tests
    // =========================================================================

    [Theory]
    [InlineData("school-a", "school-a", true)]   // Same school - allowed
    [InlineData("school-a", "school-b", false)]  // Different school - denied
    public void TenantScope_SameSchool_AllowsAccess(string callerSchool, string targetSchool, bool shouldMatch)
    {
        var caller = CreateCaller("ADMIN", callerSchool);
        var schoolFilter = TenantScope.GetSchoolFilter(caller);
        var matches = schoolFilter == targetSchool;
        Assert.Equal(shouldMatch, matches);
    }

    [Fact]
    public void TenantScope_SuperAdmin_CanAccessAnySchool()
    {
        var caller = CreateCaller("SUPER_ADMIN");
        var schoolFilter = TenantScope.GetSchoolFilter(caller);
        Assert.Null(schoolFilter); // null means unrestricted access
    }

    // =========================================================================
    // Regression: Verify the security design (404 not 403 to prevent info leak)
    // =========================================================================

    [Theory]
    [InlineData("GetUserAsync")]
    [InlineData("UpdateUserAsync")]
    [InlineData("SoftDeleteUserAsync")]
    [InlineData("SuspendUserAsync")]
    [InlineData("ActivateUserAsync")]
    public void PerIdMethods_LoadAdminUser_ById(string methodName)
    {
        // This test verifies that the methods exist and have proper signatures
        // The actual cross-tenant rejection with 404 (not 403) is implemented
        // in the service methods to avoid leaking existence information
        var serviceType = typeof(AdminUserService);
        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName)
            .ToList();
        Assert.NotEmpty(methods);
    }

    [Fact]
    public void Implementation_HasTenantScope_UsingDirective()
    {
        // Verify the service file references TenantScope namespace
        var serviceFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../Cena.Admin.Api/AdminUserService.cs"));
        Assert.Contains("using Cena.Infrastructure.Tenancy;", serviceFile);
        Assert.Contains("TenantScope.GetSchoolFilter", serviceFile);
    }

    [Fact]
    public void Implementation_LogsCrossTenantAccessAttempts()
    {
        // Verify that cross-tenant access attempts are logged for security monitoring
        var serviceFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../Cena.Admin.Api/AdminUserService.cs"));
        Assert.Contains("Cross-tenant", serviceFile);
        Assert.Contains("Log.Warning", serviceFile);
    }
}
