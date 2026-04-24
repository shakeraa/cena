// =============================================================================
// Cena Platform — Classroom-assigned ExamTarget teacher endpoint (PRR-236)
//
//   POST /api/admin/institutes/{instituteId}/classrooms/{classroomId}/assigned-targets
//
// Teacher-only route that assigns a single ExamTarget to every currently-
// enrolled student in a classroom. Per PRR-236 DoD + ADR-0050 §Q3 + ADR-0001:
//
//   - Auth: ROLE=TEACHER (SUPER_ADMIN allowed for ops) and the caller must
//     own the classroom (TeacherId or Mentor on the ClassroomDocument).
//   - Tenant scope: the route's {instituteId} must match the classroom's
//     InstituteId, else 404 (no existence leak to scoped callers).
//   - Fan-out: one ExamTargetAdded_V1 per student with
//     Source=Classroom, AssignedById=teacher, EnrollmentId=classroom-<cid>.
//   - Idempotency: re-posting with the same (examCode, track, sitting)
//     tuple returns the same summary body with StudentsAssigned=0 +
//     StudentsAlreadyAssigned=roster.Count.
//   - Audit: one AuditEventDocument per call — the downstream per-student
//     records live in the aggregate event streams (the primary audit
//     surface for student-level writes).
//
// The heavy lifting is in Cena.Actors.StudentPlan.ClassroomTargetAssignment
// (IClassroomTargetAssignmentService); this file is the HTTP shell +
// authz guard + audit write.
// =============================================================================

using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.StudentPlan;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>
/// Admin endpoints for PRR-236 classroom-assigned target teacher UI.
/// </summary>
public static class ClassroomTargetEndpoints
{
    private sealed class LoggerMarker { }

    private static readonly JsonSerializerOptions AuditJsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Public route template; kept here (not inline) so integration tests
    /// don't have to string-match.
    /// </summary>
    public const string RouteTemplate =
        "/api/admin/institutes/{instituteId}/classrooms/{classroomId}/assigned-targets";

    /// <summary>Register the endpoint group.</summary>
    public static IEndpointRouteBuilder MapClassroomTargetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(RouteTemplate, AssignAsync)
            .WithName("AssignClassroomTarget")
            .WithTags("Teacher", "ExamTargets")
            .RequireAuthorization()
            .Produces<AssignClassroomTargetResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    // ── Handler ──────────────────────────────────────────────────────────

    private static async Task<IResult> AssignAsync(
        string instituteId,
        string classroomId,
        [FromBody] AssignClassroomTargetRequestDto? body,
        HttpContext ctx,
        IDocumentStore store,
        IClassroomTargetAssignmentService assignmentService,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(instituteId))
            return BadRequest("instituteId is required.");
        if (string.IsNullOrWhiteSpace(classroomId))
            return BadRequest("classroomId is required.");
        if (body is null)
            return BadRequest("request body is required.");

        if (!TryBuildCommand(body, instituteId, classroomId,
                GetCallerId(ctx.User), out var cmd, out var err))
        {
            return BadRequest(err);
        }

        // Tenant + role + ownership guard. 404 if the classroom does not
        // live in the given institute (no existence leak). 403 if the
        // teacher doesn't own it.
        await using var session = store.QuerySession();
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.ClassroomId == classroomId, ct);

        if (classroom is null)
            return NotFound($"classroom '{classroomId}' not found");
        if (!string.Equals(classroom.InstituteId, instituteId, StringComparison.Ordinal))
            return NotFound(
                $"classroom '{classroomId}' not found in institute '{instituteId}'");

        ClassroomTargetAuthz.VerifyTeacherOwnership(ctx.User, classroom);

        var result = await assignmentService.AssignAsync(cmd, ct).ConfigureAwait(false);
        sw.Stop();

        // One audit row per call. Per-student detail already lives in the
        // aggregate event streams via ExamTargetAdded_V1 — we capture the
        // teacher-level summary here for the admin dashboard.
        await WriteAuditAsync(
            caller: ctx.User,
            instituteId: instituteId,
            classroom: classroom,
            cmd: cmd,
            result: result,
            store: store,
            logger: logger);

        logger.LogInformation(
            "[PRR-236] classroom-target-assign institute={Iid} classroom={Cid} " +
            "examCode={ExamCode} roster={Roster} assigned={Assigned} already={Already} failed={Failed} latencyMs={Ms}",
            instituteId, classroomId,
            cmd.ExamCode.Value, result.RosterSize,
            result.StudentsAssigned, result.StudentsAlreadyAssigned,
            result.StudentsFailed, sw.ElapsedMilliseconds);

        return Results.Ok(ResponseFrom(result));
    }

    // ── Command mapping ──────────────────────────────────────────────────

    /// <summary>
    /// Map + validate the wire DTO into the aggregate command. Kept
    /// internal so the endpoint test can exercise the pure mapper
    /// without standing up a web host.
    /// </summary>
    internal static bool TryBuildCommand(
        AssignClassroomTargetRequestDto dto,
        string instituteId,
        string classroomId,
        string? teacherUserId,
        out AssignClassroomTargetCommand cmd,
        out string error)
    {
        cmd = null!;
        error = "";
        if (string.IsNullOrWhiteSpace(teacherUserId))
        {
            error = "teacher user id could not be resolved from claims."; return false;
        }
        if (string.IsNullOrWhiteSpace(dto.ExamCode))
        {
            error = "examCode is required."; return false;
        }
        if (dto.Sitting is null || string.IsNullOrWhiteSpace(dto.Sitting.AcademicYear))
        {
            error = "sitting.academicYear is required."; return false;
        }
        if (dto.WeeklyHoursDefault < ExamTarget.MinWeeklyHours
            || dto.WeeklyHoursDefault > ExamTarget.MaxWeeklyHours)
        {
            error =
                $"weeklyHoursDefault must be between {ExamTarget.MinWeeklyHours} and {ExamTarget.MaxWeeklyHours}.";
            return false;
        }

        var sitting = new SittingCode(
            dto.Sitting.AcademicYear!,
            dto.Sitting.Season,
            dto.Sitting.Moed);

        cmd = new AssignClassroomTargetCommand(
            InstituteId: instituteId,
            ClassroomId: classroomId,
            TeacherUserId: new UserId(teacherUserId),
            ExamCode: new ExamCode(dto.ExamCode!),
            Track: string.IsNullOrWhiteSpace(dto.Track) ? null : new TrackCode(dto.Track!),
            Sitting: sitting,
            WeeklyHoursDefault: dto.WeeklyHoursDefault,
            QuestionPaperCodes: dto.QuestionPaperCodes);
        return true;
    }

    // ── Audit ────────────────────────────────────────────────────────────

    private static async Task WriteAuditAsync(
        ClaimsPrincipal caller,
        string instituteId,
        ClassroomDocument classroom,
        AssignClassroomTargetCommand cmd,
        AssignClassroomTargetResult result,
        IDocumentStore store,
        ILogger logger)
    {
        var adminUserId = GetCallerId(caller) ?? "anonymous";
        var adminName = caller.FindFirstValue(ClaimTypes.Name)
            ?? caller.FindFirstValue("name")
            ?? caller.FindFirstValue("email")
            ?? string.Empty;
        var adminRole = caller.FindFirstValue(ClaimTypes.Role)
            ?? caller.FindFirstValue("role")
            ?? "unknown";
        var traceId = Activity.Current?.TraceId.ToString() ?? "no-trace";

        try
        {
            var metadata = JsonSerializer.Serialize(new
            {
                classroom_id = classroom.ClassroomId,
                classroom_name = classroom.Name,
                institute_id = instituteId,
                exam_code = cmd.ExamCode.Value,
                track = cmd.Track?.Value,
                sitting = new
                {
                    academic_year = cmd.Sitting.AcademicYear,
                    season = cmd.Sitting.Season.ToString(),
                    moed = cmd.Sitting.Moed.ToString(),
                },
                weekly_hours_default = cmd.WeeklyHoursDefault,
                question_paper_codes = cmd.QuestionPaperCodes,
                roster_size = result.RosterSize,
                students_assigned = result.StudentsAssigned,
                students_already_assigned = result.StudentsAlreadyAssigned,
                students_failed = result.StudentsFailed,
                warning = result.Warning,
                trace_id = traceId,
            }, AuditJsonOpts);

            await using var session = store.LightweightSession();
            session.Store(new AuditEventDocument
            {
                Id = $"audit:admin-action:classroom-target-assign:{Guid.NewGuid():N}",
                Timestamp = DateTimeOffset.UtcNow,
                EventType = "admin_action",
                UserId = adminUserId,
                UserName = adminName,
                UserRole = adminRole,
                TenantId = instituteId,
                Action = "classroom.assigned_target",
                TargetType = "classroom",
                TargetId = classroom.ClassroomId,
                Description = result.Warning == "roster-empty"
                    ? $"Classroom target assignment skipped: empty roster for {cmd.ExamCode.Value}."
                    : $"Classroom target assigned: {result.StudentsAssigned} new, " +
                      $"{result.StudentsAlreadyAssigned} already-assigned, {result.StudentsFailed} failed " +
                      $"for {cmd.ExamCode.Value}.",
                IpAddress = string.Empty,
                UserAgent = string.Empty,
                Success = result.StudentsFailed == 0,
                ErrorMessage = null,
                MetadataJson = metadata,
            });
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PRR-236] failed to persist classroom-target audit row for classroom={Cid}",
                classroom.ClassroomId);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string? GetCallerId(ClaimsPrincipal caller)
        => caller.FindFirstValue("user_id")
           ?? caller.FindFirstValue("sub")
           ?? caller.FindFirstValue(ClaimTypes.NameIdentifier);

    private static AssignClassroomTargetResponse ResponseFrom(AssignClassroomTargetResult r)
        => new(
            ClassroomId: r.ClassroomId,
            ExamCode: r.ExamCode,
            RosterSize: r.RosterSize,
            StudentsAssigned: r.StudentsAssigned,
            StudentsAlreadyAssigned: r.StudentsAlreadyAssigned,
            StudentsFailed: r.StudentsFailed,
            Warning: r.Warning,
            PerStudent: r.PerStudentResults
                .Select(o => new AssignClassroomTargetStudentResponse(
                    StudentAnonId: o.StudentAnonId,
                    Kind: o.Kind.ToString(),
                    TargetId: o.TargetId?.Value,
                    Error: o.Error?.ToString()))
                .ToList());

    private static IResult BadRequest(string message)
        => Results.BadRequest(new CenaError(
            ErrorCodes.CENA_INTERNAL_VALIDATION,
            message,
            ErrorCategory.Validation,
            null, null));

    private static IResult NotFound(string message)
        => Results.NotFound(new CenaError(
            ErrorCodes.CENA_INTERNAL_ERROR,
            message,
            ErrorCategory.NotFound,
            null, null));
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

/// <summary>Sitting on the wire (mirrors LegacySittingCodeDto shape).</summary>
public sealed record AssignClassroomTargetSittingDto(
    string? AcademicYear,
    SittingSeason Season,
    SittingMoed Moed);

/// <summary>Request body for POST .../assigned-targets.</summary>
public sealed record AssignClassroomTargetRequestDto(
    string? ExamCode,
    string? Track,
    AssignClassroomTargetSittingDto? Sitting,
    int WeeklyHoursDefault,
    IReadOnlyList<string>? QuestionPaperCodes);

/// <summary>Response body — teacher-level summary.</summary>
public sealed record AssignClassroomTargetResponse(
    string ClassroomId,
    string ExamCode,
    int RosterSize,
    int StudentsAssigned,
    int StudentsAlreadyAssigned,
    int StudentsFailed,
    string? Warning,
    IReadOnlyList<AssignClassroomTargetStudentResponse> PerStudent);

/// <summary>Per-student outcome in the response.</summary>
public sealed record AssignClassroomTargetStudentResponse(
    string StudentAnonId,
    string Kind,
    string? TargetId,
    string? Error);
