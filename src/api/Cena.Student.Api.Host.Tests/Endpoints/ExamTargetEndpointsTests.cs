// =============================================================================
// Cena Platform — ExamTargetEndpoints validation tests (prr-218)
//
// Pure validation + error-mapping tests. HTTP-level integration is
// deferred to the existing StudentApi integration fixture when revived
// (MeEndpointsTests is currently excluded — see csproj note).
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Student.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class ExamTargetEndpointsTests
{
    // ── Add validation ───────────────────────────────────────────────────

    [Fact]
    public void Add_rejects_null_request()
    {
        var ok = ExamTargetEndpoints.TryValidateAdd(null, out var err);
        Assert.False(ok);
        Assert.Contains("required", err);
    }

    [Fact]
    public void Add_rejects_missing_examCode()
    {
        var req = new AddExamTargetRequestDto(
            ExamCode: "",
            Track: "5U",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: null);
        var ok = ExamTargetEndpoints.TryValidateAdd(req, out var err);
        Assert.False(ok);
        Assert.Contains("examCode", err);
    }

    [Fact]
    public void Add_rejects_missing_sitting_academic_year()
    {
        var req = new AddExamTargetRequestDto(
            ExamCode: "BAGRUT_MATH_5U",
            Track: "5U",
            Sitting: new SittingCodeDto("", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: null);
        var ok = ExamTargetEndpoints.TryValidateAdd(req, out var err);
        Assert.False(ok);
        Assert.Contains("academicYear", err);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(41)]
    [InlineData(10000)]
    public void Add_rejects_weekly_hours_out_of_range(int weeklyHours)
    {
        var req = new AddExamTargetRequestDto(
            ExamCode: "BAGRUT_MATH_5U",
            Track: "5U",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: weeklyHours,
            ReasonTag: null);
        var ok = ExamTargetEndpoints.TryValidateAdd(req, out var err);
        Assert.False(ok);
        Assert.Contains("weeklyHours", err);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(40)]
    public void Add_accepts_valid_weekly_hours(int weeklyHours)
    {
        var req = new AddExamTargetRequestDto(
            ExamCode: "BAGRUT_MATH_5U",
            Track: "5U",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: weeklyHours,
            ReasonTag: null);
        var ok = ExamTargetEndpoints.TryValidateAdd(req, out var err);
        Assert.True(ok, err);
    }

    [Fact]
    public void Add_accepts_null_track()
    {
        var req = new AddExamTargetRequestDto(
            ExamCode: "PET",
            Track: null,
            Sitting: new SittingCodeDto("2026", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 3,
            ReasonTag: null);
        var ok = ExamTargetEndpoints.TryValidateAdd(req, out var err);
        Assert.True(ok, err);
    }

    [Fact]
    public void Add_rejects_invalid_reason_tag_enum()
    {
        var req = new AddExamTargetRequestDto(
            ExamCode: "BAGRUT_MATH_5U",
            Track: "5U",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: (ReasonTag)999);
        var ok = ExamTargetEndpoints.TryValidateAdd(req, out var err);
        Assert.False(ok);
        Assert.Contains("reasonTag", err);
    }

    // ── Update validation ────────────────────────────────────────────────

    [Fact]
    public void Update_rejects_null_request()
    {
        var ok = ExamTargetEndpoints.TryValidateUpdate(null, out var err);
        Assert.False(ok);
    }

    [Fact]
    public void Update_rejects_out_of_range_hours()
    {
        var req = new UpdateExamTargetRequestDto(
            Track: "4U",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 100,
            ReasonTag: null);
        var ok = ExamTargetEndpoints.TryValidateUpdate(req, out var err);
        Assert.False(ok);
    }

    [Fact]
    public void Update_accepts_valid_body()
    {
        var req = new UpdateExamTargetRequestDto(
            Track: "4U",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Winter, SittingMoed.B),
            WeeklyHours: 6,
            ReasonTag: ReasonTag.Enrichment);
        var ok = ExamTargetEndpoints.TryValidateUpdate(req, out var err);
        Assert.True(ok, err);
    }

    // ── Error mapping ────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandError.ActiveTargetCapExceeded, StatusCodes.Status409Conflict)]
    [InlineData(CommandError.WeeklyBudgetExceeded, StatusCodes.Status409Conflict)]
    [InlineData(CommandError.DuplicateTarget, StatusCodes.Status409Conflict)]
    [InlineData(CommandError.TargetNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CommandError.TargetArchived, StatusCodes.Status400BadRequest)]
    [InlineData(CommandError.WeeklyHoursOutOfRange, StatusCodes.Status400BadRequest)]
    [InlineData(CommandError.SourceAssignmentMismatch, StatusCodes.Status400BadRequest)]
    public void MapError_maps_each_enum_to_expected_status(CommandError err, int expected)
    {
        var (status, _, _) = ExamTargetEndpoints.MapError(err);
        Assert.Equal(expected, status);
    }

    // ── DTO projection ───────────────────────────────────────────────────

    [Fact]
    public void ExamTargetResponseDto_From_projects_target()
    {
        var target = new ExamTarget(
            Id: new ExamTargetId("et-1"),
            Source: ExamTargetSource.Student,
            AssignedById: new UserId("stu-1"),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: new SittingCode("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: null,
            CreatedAt: DateTimeOffset.Parse("2026-04-21T10:00:00Z"),
            ArchivedAt: null);

        var dto = ExamTargetResponseDto.From(target);

        Assert.Equal("et-1", dto.Id);
        Assert.Equal(ExamTargetSource.Student, dto.Source);
        Assert.Equal("stu-1", dto.AssignedById);
        Assert.Null(dto.EnrollmentId);
        Assert.Equal("BAGRUT_MATH_5U", dto.ExamCode);
        Assert.Equal("5U", dto.Track);
        Assert.Equal("תשפ״ו", dto.Sitting.AcademicYear);
        Assert.Equal(5, dto.WeeklyHours);
        Assert.True(dto.IsActive);
    }
}
