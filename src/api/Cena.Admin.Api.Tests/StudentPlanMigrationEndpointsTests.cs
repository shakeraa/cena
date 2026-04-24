// =============================================================================
// Cena Platform — StudentPlanMigrationEndpoints DTO-mapping tests (prr-219)
//
// Pure DTO-mapping tests for the admin migration batch endpoint. HTTP-
// level integration is deferred to the existing Admin.Api integration
// fixture; these tests exercise the shape-validation surface the
// endpoint runs before it touches the service.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Admin.Api.Host.Endpoints;
using Xunit;

namespace Cena.Admin.Api.Tests;

public sealed class StudentPlanMigrationEndpointsTests
{
    private static LegacyStudentPlanSnapshotDto SampleDto(
        string? sourceId = "legacy-1",
        string? studentId = "stu-a",
        string? tenantId = "inst-001",
        string? examCode = "BAGRUT_MATH_5U",
        LegacySittingCodeDto? sitting = null)
        => new(
            MigrationSourceId: sourceId,
            StudentAnonId: studentId,
            TenantId: tenantId,
            LegacyDeadlineUtc: DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
            LegacyWeeklyBudgetHours: 5,
            InferredExamCode: examCode,
            InferredTrack: "5U",
            InferredSitting: sitting ?? new LegacySittingCodeDto(
                "תשפ״ו", SittingSeason.Summer, SittingMoed.A));

    [Fact]
    public void TryMap_success_produces_snapshot()
    {
        var dto = SampleDto();
        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out var snap, out var err);

        Assert.True(ok, err);
        Assert.Equal("legacy-1", snap.MigrationSourceId);
        Assert.Equal("stu-a", snap.StudentAnonId);
        Assert.Equal("BAGRUT_MATH_5U", snap.InferredExamCode.Value);
        Assert.Equal("5U", snap.InferredTrack?.Value);
        Assert.NotNull(snap.InferredSitting);
    }

    [Fact]
    public void TryMap_rejects_missing_source_id()
    {
        var dto = SampleDto(sourceId: "");
        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out _, out var err);
        Assert.False(ok);
        Assert.Contains("migrationSourceId", err);
    }

    [Fact]
    public void TryMap_rejects_missing_student_id()
    {
        var dto = SampleDto(studentId: "");
        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out _, out var err);
        Assert.False(ok);
        Assert.Contains("studentAnonId", err);
    }

    [Fact]
    public void TryMap_rejects_missing_tenant_id()
    {
        var dto = SampleDto(tenantId: null);
        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out _, out var err);
        Assert.False(ok);
        Assert.Contains("tenantId", err);
    }

    [Fact]
    public void TryMap_rejects_missing_exam_code()
    {
        var dto = SampleDto(examCode: "");
        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out _, out var err);
        Assert.False(ok);
        Assert.Contains("inferredExamCode", err);
    }

    [Fact]
    public void TryMap_rejects_sitting_with_missing_year()
    {
        var dto = SampleDto(sitting: new LegacySittingCodeDto("", SittingSeason.Winter, SittingMoed.A));
        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out _, out var err);
        Assert.False(ok);
        Assert.Contains("academicYear", err);
    }

    [Fact]
    public void TryMap_accepts_null_sitting()
    {
        var dto = new LegacyStudentPlanSnapshotDto(
            MigrationSourceId: "legacy-1",
            StudentAnonId: "stu-a",
            TenantId: "inst-001",
            LegacyDeadlineUtc: null,
            LegacyWeeklyBudgetHours: null,
            InferredExamCode: "PET",
            InferredTrack: null,
            InferredSitting: null);

        var ok = StudentPlanMigrationEndpoints.TryMap(dto, out var snap, out var err);

        Assert.True(ok, err);
        Assert.Null(snap.InferredSitting);
        Assert.Null(snap.InferredTrack);
    }
}
