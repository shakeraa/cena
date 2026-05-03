// =============================================================================
// Cena Platform — ClassroomTargetEndpoints unit + authz tests (PRR-236)
//
// DTO-mapping + authorisation-guard tests for the classroom-target teacher
// endpoint. HTTP-level integration (DI, Marten, SignalR) is covered by the
// integration test suite; these tests exercise the pure surface the
// endpoint runs before I/O.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.StudentPlan;
using Cena.Admin.Api.Host.Endpoints;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Xunit;

namespace Cena.Admin.Api.Tests;

public sealed class ClassroomTargetEndpointsTests
{
    // ── TryBuildCommand ─────────────────────────────────────────────────

    private static AssignClassroomTargetRequestDto SampleDto(
        string? examCode = "BAGRUT_MATH_5U",
        string? track = "5U",
        int weeklyHours = 4,
        AssignClassroomTargetSittingDto? sitting = null,
        IReadOnlyList<string>? papers = null)
        => new(
            ExamCode: examCode,
            Track: track,
            Sitting: sitting ?? new AssignClassroomTargetSittingDto(
                "תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHoursDefault: weeklyHours,
            QuestionPaperCodes: papers ?? new[] { "035581" });

    [Fact]
    public void TryBuildCommand_success()
    {
        var dto = SampleDto();
        var ok = ClassroomTargetEndpoints.TryBuildCommand(
            dto, "inst-1", "class-1", "teacher-x", out var cmd, out var err);

        Assert.True(ok, err);
        Assert.Equal("inst-1", cmd.InstituteId);
        Assert.Equal("class-1", cmd.ClassroomId);
        Assert.Equal("teacher-x", cmd.TeacherUserId.Value);
        Assert.Equal("BAGRUT_MATH_5U", cmd.ExamCode.Value);
        Assert.Equal("5U", cmd.Track?.Value);
        Assert.Equal(4, cmd.WeeklyHoursDefault);
        Assert.Single(cmd.QuestionPaperCodes!);
    }

    [Fact]
    public void TryBuildCommand_rejects_missing_teacher_id()
    {
        var dto = SampleDto();
        var ok = ClassroomTargetEndpoints.TryBuildCommand(
            dto, "inst-1", "class-1", null, out _, out var err);
        Assert.False(ok);
        Assert.Contains("teacher", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildCommand_rejects_missing_exam_code()
    {
        var dto = SampleDto(examCode: "");
        var ok = ClassroomTargetEndpoints.TryBuildCommand(
            dto, "inst-1", "class-1", "teacher-x", out _, out var err);
        Assert.False(ok);
        Assert.Contains("examCode", err);
    }

    [Fact]
    public void TryBuildCommand_rejects_missing_sitting_year()
    {
        var dto = SampleDto(sitting: new AssignClassroomTargetSittingDto(
            "", SittingSeason.Summer, SittingMoed.A));
        var ok = ClassroomTargetEndpoints.TryBuildCommand(
            dto, "inst-1", "class-1", "teacher-x", out _, out var err);
        Assert.False(ok);
        Assert.Contains("academicYear", err);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(41)]
    public void TryBuildCommand_rejects_weekly_hours_out_of_range(int hours)
    {
        var dto = SampleDto(weeklyHours: hours);
        var ok = ClassroomTargetEndpoints.TryBuildCommand(
            dto, "inst-1", "class-1", "teacher-x", out _, out var err);
        Assert.False(ok);
        Assert.Contains("weeklyHoursDefault", err);
    }

    [Fact]
    public void TryBuildCommand_allows_null_track()
    {
        var dto = SampleDto(track: null);
        var ok = ClassroomTargetEndpoints.TryBuildCommand(
            dto, "inst-1", "class-1", "teacher-x", out var cmd, out var err);
        Assert.True(ok, err);
        Assert.Null(cmd.Track);
    }

    // ── ClassroomTargetAuthz ────────────────────────────────────────────

    private static ClaimsPrincipal User(string role, string userId = "u-1")
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("user_id", userId),
        }, "test");
        return new ClaimsPrincipal(identity);
    }

    private static ClassroomDocument Classroom(
        string teacherId = "teacher-42",
        string[]? mentors = null,
        string? instituteId = "inst-1")
        => new()
        {
            ClassroomId = "class-1",
            TeacherId = teacherId,
            MentorIds = mentors ?? Array.Empty<string>(),
            InstituteId = instituteId,
        };

    [Fact]
    public void Authz_allows_super_admin()
    {
        ClassroomTargetAuthz.VerifyTeacherOwnership(
            User("SUPER_ADMIN", "root"), Classroom());
    }

    [Fact]
    public void Authz_allows_owning_teacher()
    {
        ClassroomTargetAuthz.VerifyTeacherOwnership(
            User("TEACHER", "teacher-42"), Classroom(teacherId: "teacher-42"));
    }

    [Fact]
    public void Authz_allows_mentor_on_classroom()
    {
        ClassroomTargetAuthz.VerifyTeacherOwnership(
            User("TEACHER", "mentor-7"),
            Classroom(teacherId: "teacher-42", mentors: new[] { "mentor-7" }));
    }

    [Fact]
    public void Authz_rejects_other_teacher()
    {
        var ex = Assert.Throws<ForbiddenException>(() =>
            ClassroomTargetAuthz.VerifyTeacherOwnership(
                User("TEACHER", "stranger"),
                Classroom(teacherId: "teacher-42")));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    [Fact]
    public void Authz_rejects_admin_role()
    {
        // ADMIN is NOT permitted — PRR-236 is teacher-specific. Institute-
        // wide assignment is a separate path.
        var ex = Assert.Throws<ForbiddenException>(() =>
            ClassroomTargetAuthz.VerifyTeacherOwnership(
                User("ADMIN", "admin-1"), Classroom()));
        Assert.Equal(ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE, ex.ErrorCode);
    }

    [Fact]
    public void Authz_rejects_student_role()
    {
        Assert.Throws<ForbiddenException>(() =>
            ClassroomTargetAuthz.VerifyTeacherOwnership(
                User("STUDENT", "stu-1"), Classroom()));
    }

    [Fact]
    public void Authz_rejects_unknown_role()
    {
        Assert.Throws<ForbiddenException>(() =>
            ClassroomTargetAuthz.VerifyTeacherOwnership(
                User("MODERATOR", "m-1"), Classroom()));
    }

    // ── Route template stability ────────────────────────────────────────

    [Fact]
    public void Route_template_is_versioned_under_admin_institutes_classrooms()
    {
        // The route shape is part of the public API contract; guard against
        // silent renames by asserting the exact pattern the frontend consumes.
        Assert.Equal(
            "/api/admin/institutes/{instituteId}/classrooms/{classroomId}/assigned-targets",
            ClassroomTargetEndpoints.RouteTemplate);
    }
}
