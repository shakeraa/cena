// =============================================================================
// Cena Platform — TeacherHeatmapScopeGuard tests (RDY-070 Phase 1A, IDOR)
//
// Covers the cross-teacher IDOR regression explicitly called out in the
// task body: "teacher A cannot see teacher B's classroom". Tests the
// scoping helper directly so the regression is fast and does not require
// booting the web host.
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Features.TeacherConsole;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api.Tests.TeacherConsole;

[Trait("Category", "IDOR")]
public class TeacherHeatmapScopeGuardTests
{
    private static ClaimsPrincipal MakeUser(string role, string sub, string? schoolId = null)
    {
        var claims = new List<Claim>
        {
            new("sub", sub),
            new(ClaimTypes.Role, role),
        };
        if (schoolId is not null) claims.Add(new Claim("school_id", schoolId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClassroomDocument MakeClassroom(
        string classroomId = "c-9a",
        string teacherId = "teacher-ofir",
        string instituteId = "inst-north-high",
        ClassroomMode mode = ClassroomMode.InstructorLed,
        string[]? mentors = null)
        => new()
        {
            Id = classroomId,
            ClassroomId = classroomId,
            TeacherId = teacherId,
            InstituteId = instituteId,
            Mode = mode,
            MentorIds = mentors ?? Array.Empty<string>(),
        };

    // ── Happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Teacher_OwningClassroom_Passes()
    {
        var teacherA = MakeUser("TEACHER", "teacher-ofir");
        var classroom = MakeClassroom(teacherId: "teacher-ofir");

        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(teacherA, classroom);
    }

    [Fact]
    public void Teacher_AsMentor_Passes()
    {
        var mentor = MakeUser("TEACHER", "mentor-dana");
        var classroom = MakeClassroom(
            teacherId: "teacher-ofir",
            mentors: new[] { "mentor-dana" });

        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(mentor, classroom);
    }

    [Fact]
    public void Admin_InSameInstitute_Passes()
    {
        var admin = MakeUser("ADMIN", "admin-1", schoolId: "inst-north-high");
        var classroom = MakeClassroom(instituteId: "inst-north-high");

        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(admin, classroom);
    }

    [Fact]
    public void SuperAdmin_AnyInstitute_Passes()
    {
        var superAdmin = MakeUser("SUPER_ADMIN", "platform-owner");
        var classroom = MakeClassroom(instituteId: "inst-unknown");

        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(superAdmin, classroom);
    }

    // ── IDOR regressions ─────────────────────────────────────────────────────

    [Fact]
    public void Teacher_CannotSeeOtherTeachersClassroom()
    {
        var teacherB = MakeUser("TEACHER", "teacher-amjad");
        var teacherAClassroom = MakeClassroom(teacherId: "teacher-ofir");

        var ex = Assert.Throws<ForbiddenException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(teacherB, teacherAClassroom));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("teacher-amjad", ex.Message);
        Assert.Contains("teacher-ofir",  ex.Message);
    }

    [Theory]
    [InlineData("teacher-zohar")]
    [InlineData("teacher-rana")]
    [InlineData("attacker")]
    [InlineData("")]
    public void Teacher_CrossTeacherMatrix_AllBlocked(string impersonatorSub)
    {
        var imposter = MakeUser("TEACHER", impersonatorSub);
        var classroom = MakeClassroom(teacherId: "teacher-ofir");

        Assert.Throws<ForbiddenException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(imposter, classroom));
    }

    [Fact]
    public void Admin_CrossInstitute_Blocked()
    {
        var admin = MakeUser("ADMIN", "admin-1", schoolId: "inst-south-academy");
        var classroom = MakeClassroom(instituteId: "inst-north-high");

        var ex = Assert.Throws<ForbiddenException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(admin, classroom));

        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    [Fact]
    public void Admin_WithoutSchoolClaim_Blocked()
    {
        var admin = MakeUser("ADMIN", "admin-1");
        var classroom = MakeClassroom(instituteId: "inst-north-high");

        Assert.Throws<ForbiddenException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(admin, classroom));
    }

    [Theory]
    [InlineData("STUDENT")]
    [InlineData("PARENT")]
    [InlineData("")]
    [InlineData("UNKNOWN_ROLE")]
    public void NonPrivilegedRoles_AllBlocked(string role)
    {
        var user = MakeUser(role, "user-1", schoolId: "inst-north-high");
        var classroom = MakeClassroom(instituteId: "inst-north-high");

        Assert.Throws<ForbiddenException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(user, classroom));
    }

    // ── Argument validation ──────────────────────────────────────────────────

    [Fact]
    public void NullCaller_ThrowsArgumentNullException()
    {
        var classroom = MakeClassroom();
        Assert.Throws<ArgumentNullException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(null!, classroom));
    }

    [Fact]
    public void NullClassroom_ThrowsArgumentNullException()
    {
        var teacher = MakeUser("TEACHER", "teacher-ofir");
        Assert.Throws<ArgumentNullException>(() =>
            TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(teacher, null!));
    }
}
