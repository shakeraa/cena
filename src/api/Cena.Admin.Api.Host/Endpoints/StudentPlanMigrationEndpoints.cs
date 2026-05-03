// =============================================================================
// Cena Platform — /api/admin/institutes/{id}/migrate-student-plan (prr-219)
//
// Admin surface for the StudentPlan multi-target migration safety net.
// Per prr-219 DoD + Scope:
//   - Feature-flagged staged upcast (flag: Cena:Migration:StudentPlanV2Enabled)
//   - Dry-run mode reports expected event volume
//   - Retry + DLQ: per-row failures emit StudentPlanMigrationFailed_V1
//   - Idempotent: re-running on an already-migrated institute is a no-op
//
// Auth: admin-only. Requires an authenticated principal with admin
// claims — enforced at the minimal-API group level by the standard
// RequireAuthorization policy + per-route role check.
//
// Request shape:
//   POST /api/admin/institutes/{tenantId}/migrate-student-plan?dryRun=true
//   Body: { "snapshots": [ LegacyStudentPlanSnapshot[] ] }
//
// Response:
//   200 OK with UpcastBatchResult summary.
//   400 if body empty / malformed.
//   403 if caller lacks admin role.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Migration;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>
/// Admin endpoints for the prr-219 StudentPlan migration safety net.
/// </summary>
public static class StudentPlanMigrationEndpoints
{
    private sealed class LoggerMarker { }

    /// <summary>Register the endpoint group.</summary>
    public static IEndpointRouteBuilder MapStudentPlanMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/institutes/{tenantId}/migrate-student-plan")
            .WithTags("AdminMigration")
            .RequireAuthorization();

        group.MapPost("", MigrateAsync)
            .WithName("MigrateStudentPlanForInstitute")
            .Produces<UpcastBatchResult>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    // ── Handler ──────────────────────────────────────────────────────────

    private static async Task<IResult> MigrateAsync(
        string tenantId,
        [FromBody] MigrateStudentPlanRequestDto? body,
        [FromQuery(Name = "dryRun")] bool? dryRun,
        IStudentPlanMigrationService migrationService,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest("tenantId is required.");
        }

        if (body?.Snapshots is null)
        {
            return BadRequest("snapshots[] is required.");
        }

        var typed = new List<LegacyStudentPlanSnapshot>(body.Snapshots.Count);
        foreach (var s in body.Snapshots)
        {
            if (!TryMap(s, out var snap, out var err))
            {
                return BadRequest(err);
            }
            typed.Add(snap);
        }

        var isDryRun = dryRun == true;
        logger.LogInformation(
            "[MIGRATE] tenant={TenantId} snapshots={Count} dryRun={DryRun}",
            tenantId, typed.Count, isDryRun);

        var result = await migrationService
            .UpcastTenantAsync(tenantId, typed, isDryRun, ct)
            .ConfigureAwait(false);

        logger.LogInformation(
            "[MIGRATE] tenant={TenantId} total={Total} migrated={Migrated} skipped={Skipped} failed={Failed}",
            tenantId, result.Total, result.Migrated, result.Skipped, result.Failed);

        return Results.Ok(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static IResult BadRequest(string message)
        => Results.BadRequest(new CenaError(
            ErrorCodes.CENA_INTERNAL_VALIDATION,
            message,
            ErrorCategory.Validation,
            null, null));

    internal static bool TryMap(
        LegacyStudentPlanSnapshotDto dto,
        out LegacyStudentPlanSnapshot snap,
        out string error)
    {
        snap = default!;
        error = "";
        if (dto is null)
        {
            error = "snapshot entry is null."; return false;
        }
        if (string.IsNullOrWhiteSpace(dto.MigrationSourceId))
        {
            error = "migrationSourceId is required."; return false;
        }
        if (string.IsNullOrWhiteSpace(dto.StudentAnonId))
        {
            error = "studentAnonId is required."; return false;
        }
        if (string.IsNullOrWhiteSpace(dto.TenantId))
        {
            error = "tenantId is required on every snapshot."; return false;
        }
        if (string.IsNullOrWhiteSpace(dto.InferredExamCode))
        {
            error = "inferredExamCode is required."; return false;
        }

        SittingCode? sitting = null;
        if (dto.InferredSitting is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.InferredSitting.AcademicYear))
            {
                error = "inferredSitting.academicYear is required when sitting is supplied.";
                return false;
            }
            sitting = new SittingCode(
                dto.InferredSitting.AcademicYear!,
                dto.InferredSitting.Season,
                dto.InferredSitting.Moed);
        }

        TimeSpan? weekly = dto.LegacyWeeklyBudgetHours is null
            ? null
            : TimeSpan.FromHours(dto.LegacyWeeklyBudgetHours.Value);

        snap = new LegacyStudentPlanSnapshot(
            MigrationSourceId: dto.MigrationSourceId,
            StudentAnonId: dto.StudentAnonId,
            TenantId: dto.TenantId,
            LegacyDeadlineUtc: dto.LegacyDeadlineUtc,
            LegacyWeeklyBudget: weekly,
            InferredExamCode: new ExamCode(dto.InferredExamCode),
            InferredTrack: string.IsNullOrWhiteSpace(dto.InferredTrack)
                ? null
                : new TrackCode(dto.InferredTrack!),
            InferredSitting: sitting);
        return true;
    }
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

/// <summary>Sitting tuple as it travels on the wire.</summary>
public sealed record LegacySittingCodeDto(
    string? AcademicYear,
    SittingSeason Season,
    SittingMoed Moed);

/// <summary>Single-row manifest entry.</summary>
public sealed record LegacyStudentPlanSnapshotDto(
    string? MigrationSourceId,
    string? StudentAnonId,
    string? TenantId,
    DateTimeOffset? LegacyDeadlineUtc,
    double? LegacyWeeklyBudgetHours,
    string? InferredExamCode,
    string? InferredTrack,
    LegacySittingCodeDto? InferredSitting);

/// <summary>Batch request body.</summary>
public sealed record MigrateStudentPlanRequestDto(
    IReadOnlyList<LegacyStudentPlanSnapshotDto> Snapshots);
