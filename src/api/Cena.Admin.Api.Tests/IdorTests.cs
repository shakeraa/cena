// =============================================================================
// SEC-002: IDOR Prevention - Unit tests for ResourceOwnershipGuard
//
// Verifies that cross-student data access is blocked and same-student
// access is always allowed for STUDENT and PARENT roles, while
// ADMIN / MODERATOR / SUPER_ADMIN pass through to tenant-level scoping.
//
// Run: dotnet test --filter "Category=IDOR"
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api.Tests;

[Trait("Category", "IDOR")]
public class IdorTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static ClaimsPrincipal MakeStudent(string uid)
        => MakePrincipal(uid, "STUDENT");

    private static ClaimsPrincipal MakeParent(string uid, params string[] childIds)
    {
        var claims = new List<Claim>
        {
            new("sub", uid),
            new(ClaimTypes.Role, "PARENT"),
        };
        foreach (var child in childIds)
            claims.Add(new Claim("student_id", child));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal MakeAdmin(string uid, string schoolId)
    {
        var claims = new List<Claim>
        {
            new("sub", uid),
            new(ClaimTypes.Role, "ADMIN"),
            new("school_id", schoolId),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal MakeModerator(string uid, string schoolId)
    {
        var claims = new List<Claim>
        {
            new("sub", uid),
            new(ClaimTypes.Role, "MODERATOR"),
            new("school_id", schoolId),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal MakeSuperAdmin(string uid)
        => MakePrincipal(uid, "SUPER_ADMIN");

    private static ClaimsPrincipal MakePrincipal(string uid, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", uid),
            new(ClaimTypes.Role, role),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal MakeUnauthenticated()
        => new(new ClaimsIdentity());

    // =========================================================================
    // STUDENT: own data allowed
    // =========================================================================

    [Fact]
    public void Student_AccessingOwnData_Passes()
    {
        var student = MakeStudent("student-a");
        // Must not throw
        ResourceOwnershipGuard.VerifyStudentAccess(student, "student-a");
    }

    [Fact]
    public void Student_AccessingOtherStudentData_Throws403()
    {
        var student = MakeStudent("student-a");
        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(student, "student-b"));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void Student_AccessingOtherStudentData_ErrorMessageNamesVictim()
    {
        var student = MakeStudent("student-a");
        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(student, "victim-student"));

        Assert.Contains("victim-student", ex.Message);
    }

    // =========================================================================
    // STUDENT: IDOR matrix — many target IDs all blocked
    // =========================================================================

    [Theory]
    [InlineData("student-b")]
    [InlineData("student-c")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("admin-user")]
    [InlineData("")] // Empty string — not the caller's real uid
    public void Student_CrossStudentMatrix_AllBlocked(string targetId)
    {
        var student = MakeStudent("student-a");
        Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(student, targetId));
    }

    // =========================================================================
    // PARENT: linked child allowed, unlinked child blocked
    // =========================================================================

    [Fact]
    public void Parent_AccessingLinkedChild_Passes()
    {
        var parent = MakeParent("parent-1", "student-a", "student-b");
        ResourceOwnershipGuard.VerifyStudentAccess(parent, "student-a");
        ResourceOwnershipGuard.VerifyStudentAccess(parent, "student-b");
    }

    [Fact]
    public void Parent_AccessingUnlinkedChild_Throws403()
    {
        var parent = MakeParent("parent-1", "student-a");
        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(parent, "student-c"));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void Parent_WithNoLinkedChildren_AllBlocked()
    {
        var parent = MakeParent("parent-1"); // no children
        Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(parent, "any-student"));
    }

    // =========================================================================
    // PARENT: VerifyParentStudentLink method
    // =========================================================================

    [Fact]
    public void VerifyParentStudentLink_LinkedChild_Passes()
    {
        var parent = MakeParent("parent-1", "child-a");
        ResourceOwnershipGuard.VerifyParentStudentLink(parent, "child-a");
    }

    [Fact]
    public void VerifyParentStudentLink_UnlinkedChild_Throws403()
    {
        var parent = MakeParent("parent-1", "child-a");
        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyParentStudentLink(parent, "child-b"));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    // =========================================================================
    // ADMIN: passes through (tenant scoping happens at query layer)
    // =========================================================================

    [Fact]
    public void Admin_AccessingAnyStudentInSchool_Passes()
    {
        var admin = MakeAdmin("admin-1", "school-xyz");
        // ADMINs are not restricted at the ownership guard level
        ResourceOwnershipGuard.VerifyStudentAccess(admin, "any-student-in-school");
        ResourceOwnershipGuard.VerifyStudentAccess(admin, "student-a");
        ResourceOwnershipGuard.VerifyStudentAccess(admin, "student-b");
    }

    // =========================================================================
    // MODERATOR: same as ADMIN — passes through
    // =========================================================================

    [Fact]
    public void Moderator_AccessingAnyStudent_Passes()
    {
        var mod = MakeModerator("mod-1", "school-xyz");
        ResourceOwnershipGuard.VerifyStudentAccess(mod, "any-student");
    }

    // =========================================================================
    // SUPER_ADMIN: unrestricted
    // =========================================================================

    [Fact]
    public void SuperAdmin_AccessingAnyStudentAnySchool_Passes()
    {
        var superAdmin = MakeSuperAdmin("sa-1");
        ResourceOwnershipGuard.VerifyStudentAccess(superAdmin, "student-a");
        ResourceOwnershipGuard.VerifyStudentAccess(superAdmin, "student-school-b");
        ResourceOwnershipGuard.VerifyStudentAccess(superAdmin, "any-uuid");
    }

    // =========================================================================
    // Unknown / missing role — denied
    // =========================================================================

    [Fact]
    public void UnknownRole_AccessingStudent_Throws403()
    {
        var weirdUser = MakePrincipal("weirdly-privileged", "TEACHER"); // Not a known role
        Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(weirdUser, "student-a"));
    }

    [Fact]
    public void NoRole_AccessingStudent_Throws403()
    {
        var noRole = MakeUnauthenticated();
        Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(noRole, "student-a"));
    }

    // =========================================================================
    // Error code is always CENA_AUTH_IDOR_VIOLATION
    // =========================================================================

    [Theory]
    [InlineData("STUDENT")]
    [InlineData("PARENT")]
    public void ViolatingRoles_AlwaysThrowIdorErrorCode(string role)
    {
        ClaimsPrincipal caller = role switch
        {
            "STUDENT" => MakeStudent("student-a"),
            "PARENT"  => MakeParent("parent-1"),   // no children linked
            _         => throw new InvalidOperationException()
        };

        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(caller, "victim"));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    // =========================================================================
    // ForbiddenException is a CenaException and carries status 403
    // =========================================================================

    [Fact]
    public void ForbiddenException_IsCenaException_With403()
    {
        var student = MakeStudent("student-a");
        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(student, "student-b"));

        Assert.IsAssignableFrom<CenaException>(ex);
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(ErrorCategory.Authorization, ex.Category);
    }

    // =========================================================================
    // Claim extraction: Firebase "role" claim vs ClaimTypes.Role
    // =========================================================================

    [Fact]
    public void StudentWithFirebaseRoleClaim_NotClaimTypesRole_IsEnforced()
    {
        // Firebase JWT "role" claim (not ClaimTypes.Role)
        var claims = new List<Claim>
        {
            new("sub", "student-a"),
            new("role", "STUDENT"),  // raw Firebase claim, not ClaimTypes.Role
        };
        var student = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var ex = Assert.Throws<ForbiddenException>(
            () => ResourceOwnershipGuard.VerifyStudentAccess(student, "student-b"));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    [Fact]
    public void StudentWithFirebaseRoleClaim_AccessingOwnData_Passes()
    {
        var claims = new List<Claim>
        {
            new("sub", "student-a"),
            new("role", "STUDENT"),
        };
        var student = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        // Must not throw
        ResourceOwnershipGuard.VerifyStudentAccess(student, "student-a");
    }
}
