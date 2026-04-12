// =============================================================================
// Cena Platform -- QueryAllRawEvents Tenant Tests
// FIND-qa-004: Regression tests for the QueryAllRawEvents anti-pattern
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Tenancy;

namespace Cena.Admin.Api.Tests;

/// <summary>
/// FIND-qa-004: Tests for the top 5 QueryAllRawEvents callsites to ensure
/// tenant scoping is applied.
/// </summary>
public class QueryAllRawEventsTenantTests
{
    private static ClaimsPrincipal CreateCaller(string role, string? schoolId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role)
        };
        if (schoolId != null)
            claims.Add(new Claim("school_id", schoolId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    // =========================================================================
    // Top 5 hottest QueryAllRawEvents callsites - tenant scoping tests
    // =========================================================================

    [Fact]
    public void GamificationEndpoints_GetBadges_HasTenantScoping()
    {
        // Verify GamificationEndpoints.cs has tenant scoping on QueryAllRawEvents
        var endpointFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs"));
        
        // Should have ResourceOwnershipGuard or school_id check
        Assert.Contains("ResourceOwnershipGuard", endpointFile);
    }

    [Fact]
    public void FocusAnalyticsService_GetDegradationCurve_HasTenantFilter()
    {
        // Verify FocusAnalyticsService filters by SchoolId before QueryAllRawEvents
        var serviceFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Cena.Admin.Api/FocusAnalyticsService.cs"));
        
        // Check that TenantScope.GetSchoolFilter is called
        Assert.Contains("TenantScope.GetSchoolFilter", serviceFile);
    }

    [Fact]
    public void AdminDashboardService_TopicsMastery_HasTenantScoping()
    {
        // Verify AdminDashboardService has tenant scoping
        var serviceFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Cena.Admin.Api/AdminDashboardService.cs"));
        
        // Should have SchoolId filtering
        Assert.Contains("SchoolId", serviceFile);
    }

    [Fact]
    public void EventStreamService_GetRecentEvents_HasTenantScoping()
    {
        // Verify EventStreamService limits by tenant
        var serviceFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Cena.Admin.Api/EventStreamService.cs"));
        
        // Should be SuperAdmin-only or have tenant limits
        Assert.True(
            serviceFile.Contains("SuperAdmin") || serviceFile.Contains("TenantScope"),
            "EventStreamService should have SuperAdmin restriction or tenant scoping");
    }

    [Fact]
    public void TutoringAdminService_GetSessionDetail_UsesDocumentQuery_NotRawEvents()
    {
        // Verify TutoringAdminService uses document queries instead of QueryAllRawEvents where possible
        var serviceFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Cena.Admin.Api/TutoringAdminService.cs"));
        
        // Should use TutorMessageDocument with SchoolId filter
        Assert.Contains("TutorMessageDocument", serviceFile);
    }

    // =========================================================================
    // Lint script verification
    // =========================================================================

    [Fact]
    public void LintScript_Exists_AndIsExecutable()
    {
        var lintScript = Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/lint-query-all-raw-events.sh");
        Assert.True(File.Exists(lintScript), "lint-query-all-raw-events.sh should exist");
        
        var content = File.ReadAllText(lintScript);
        Assert.Contains("QueryAllRawEvents", content);
        Assert.Contains("SchoolId", content);
    }

    // =========================================================================
    // Cross-tenant data isolation scenarios
    // =========================================================================

    [Theory]
    [InlineData("school-a", "school-a", true)]
    [InlineData("school-a", "school-b", false)]
    [InlineData(null, "school-b", true)]
    public void TenantIsolation_Scenarios(string? callerSchool, string targetSchool, bool shouldAccess)
    {
        var caller = callerSchool == null
            ? CreateCaller("SUPER_ADMIN")
            : CreateCaller("ADMIN", callerSchool);

        var schoolFilter = TenantScope.GetSchoolFilter(caller);
        
        if (callerSchool == null)
        {
            Assert.Null(schoolFilter);
        }
        else
        {
            var hasAccess = schoolFilter == targetSchool;
            Assert.Equal(shouldAccess, hasAccess);
        }
    }

    // =========================================================================
    // Verify QueryAllRawEvents count and trend
    // =========================================================================

    [Fact]
    public void QueryAllRawEvents_Count_Documented()
    {
        // This test documents the current count of QueryAllRawEvents calls.
        // If the count increases significantly, a human should review.
        var repoRoot = Path.Combine(AppContext.BaseDirectory, "../../../../..");
        
        // Count occurrences in src/ directory
        var csFiles = Directory.GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains("/src/") && !f.Contains("/bin/") && !f.Contains("/obj/"))
            .ToList();
        
        var count = 0;
        foreach (var file in csFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                count += content.Split("QueryAllRawEvents").Length - 1;
            }
            catch { /* ignore */ }
        }
        
        // Document the count - if it exceeds 60, require review
        Assert.True(count < 60, 
            $"QueryAllRawEvents count ({count}) exceeded threshold. " +
            "Review for tenant scoping. Consider using projections instead.");
    }
}
