// =============================================================================
// Cena Platform — StudyPlanSettingsEndpoints validation tests (prr-148)
//
// Pure validation + DTO-mapping tests. HTTP-level integration is deferred
// to the existing StudentApi integration fixture when it is revived
// (MeEndpointsTests is currently excluded — see csproj note).
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Student.Api.Host.Endpoints;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class StudyPlanSettingsEndpointTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-20T10:00:00Z");

    [Fact]
    public void TryValidate_rejects_null_request()
    {
        var ok = StudyPlanSettingsEndpoints.TryValidate(null, Now, out var err);
        Assert.False(ok);
        Assert.Contains("Request body is required", err);
    }

    [Fact]
    public void TryValidate_rejects_empty_request()
    {
        var ok = StudyPlanSettingsEndpoints.TryValidate(
            new StudyPlanRequestDto(DeadlineUtc: null, WeeklyBudgetHours: null),
            Now, out var err);
        Assert.False(ok);
        Assert.Contains("At least one", err);
    }

    [Fact]
    public void TryValidate_rejects_deadline_too_close()
    {
        // Exactly 5 days ahead — below the 7-day minimum.
        var req = new StudyPlanRequestDto(
            DeadlineUtc: Now.AddDays(5),
            WeeklyBudgetHours: null);
        var ok = StudyPlanSettingsEndpoints.TryValidate(req, Now, out var err);
        Assert.False(ok);
        Assert.Contains("days in the future", err);
    }

    [Fact]
    public void TryValidate_rejects_deadline_in_the_past()
    {
        var req = new StudyPlanRequestDto(
            DeadlineUtc: Now.AddDays(-1),
            WeeklyBudgetHours: null);
        var ok = StudyPlanSettingsEndpoints.TryValidate(req, Now, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryValidate_accepts_deadline_well_in_future()
    {
        var req = new StudyPlanRequestDto(
            DeadlineUtc: Now.AddDays(90),
            WeeklyBudgetHours: null);
        var ok = StudyPlanSettingsEndpoints.TryValidate(req, Now, out var err);
        Assert.True(ok, err);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    [InlineData(-3)]
    [InlineData(40.01)]
    [InlineData(100)]
    public void TryValidate_rejects_weekly_budget_out_of_range(double hours)
    {
        var req = new StudyPlanRequestDto(
            DeadlineUtc: null,
            WeeklyBudgetHours: hours);
        var ok = StudyPlanSettingsEndpoints.TryValidate(req, Now, out var err);
        Assert.False(ok);
        Assert.Contains("weeklyBudgetHours", err);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(40)]
    public void TryValidate_accepts_weekly_budget_in_range(double hours)
    {
        var req = new StudyPlanRequestDto(
            DeadlineUtc: null,
            WeeklyBudgetHours: hours);
        var ok = StudyPlanSettingsEndpoints.TryValidate(req, Now, out var err);
        Assert.True(ok, err);
    }

    [Fact]
    public void TryValidate_accepts_both_fields_together()
    {
        var req = new StudyPlanRequestDto(
            DeadlineUtc: Now.AddDays(60),
            WeeklyBudgetHours: 8);
        var ok = StudyPlanSettingsEndpoints.TryValidate(req, Now, out var err);
        Assert.True(ok, err);
    }

    [Fact]
    public void MapToDto_converts_null_weekly_budget_to_null_hours()
    {
        var config = new StudentPlanConfig(
            StudentAnonId: "stu-1",
            DeadlineUtc: null,
            WeeklyBudget: null,
            UpdatedAt: null);

        var dto = StudyPlanSettingsEndpoints.MapToDto(config);

        Assert.Null(dto.DeadlineUtc);
        Assert.Null(dto.WeeklyBudgetHours);
        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public void MapToDto_converts_weekly_budget_to_hours_as_double()
    {
        var updated = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
        var config = new StudentPlanConfig(
            StudentAnonId: "stu-1",
            DeadlineUtc: DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
            WeeklyBudget: TimeSpan.FromHours(12.5),
            UpdatedAt: updated);

        var dto = StudyPlanSettingsEndpoints.MapToDto(config);

        Assert.Equal(DateTimeOffset.Parse("2026-07-01T08:00:00Z"), dto.DeadlineUtc);
        Assert.Equal(12.5, dto.WeeklyBudgetHours);
        Assert.Equal(updated, dto.UpdatedAt);
    }
}
