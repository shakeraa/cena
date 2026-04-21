// =============================================================================
// Cena Platform — /api/me/exam-targets endpoints (prr-218, ADR-0050)
//
// Student-facing CRUD for the multi-target exam plan. Supersedes the
// legacy /api/me/study-plan endpoints (prr-148); the legacy endpoints
// remain wired during the prr-219 migration window, then come out per
// prr-234.
//
// Wire shape (REST, ADR-0050-compliant):
//
//   GET  /api/me/exam-targets           → list active targets
//   GET  /api/me/exam-targets?includeArchived=true → include archived
//   POST /api/me/exam-targets           → add a new target
//   PUT  /api/me/exam-targets/{id}      → update an active target
//   POST /api/me/exam-targets/{id}/archive   → archive (soft-delete)
//   POST /api/me/exam-targets/{id}/complete  → mark completed
//
// Auth: student-owned; student_id claim on the JWT keys the stream.
// Rate limit: standard "api" policy (60 req/min per user).
//
// Validation (all server-enforced via the command handler's invariants;
// these endpoint-level checks are cheap shape guards that fail fast
// before touching the aggregate store):
//   - examCode non-empty catalog id
//   - track optional; when supplied, non-empty
//   - sitting.academicYear non-empty
//   - sitting.season ∈ {Summer, Winter}
//   - sitting.moed   ∈ {A, B, C, Special}
//   - weeklyHours    ∈ [1, 40]
//   - reasonTag      optional; when supplied, ∈ enum
//
// Source=Student is ALWAYS enforced at this surface — the student cannot
// add Classroom- or Tenant-scoped targets via /api/me/*. Those flow
// through the teacher / admin endpoints (EPIC-PRR-C).
// =============================================================================

using System.Security.Claims;
using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>
/// Minimal-API endpoints for the multi-target exam-plan surface.
/// </summary>
public static class ExamTargetEndpoints
{
    private sealed class LoggerMarker { }

    /// <summary>Route group name for tests / OpenAPI grouping.</summary>
    public const string GroupTag = "Me";

    /// <summary>Register the endpoint group on the application.</summary>
    public static IEndpointRouteBuilder MapExamTargetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/exam-targets")
            .WithTags(GroupTag)
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapGet("", ListAsync)
            .WithName("ListExamTargets")
            .Produces<ExamTargetListResponseDto>(StatusCodes.Status200OK);

        group.MapPost("", AddAsync)
            .WithName("AddExamTarget")
            .Produces<ExamTargetResponseDto>(StatusCodes.Status201Created)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        group.MapPut("{id}", UpdateAsync)
            .WithName("UpdateExamTarget")
            .Produces<ExamTargetResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        group.MapPost("{id}/archive", ArchiveAsync)
            .WithName("ArchiveExamTarget")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        group.MapPost("{id}/complete", CompleteAsync)
            .WithName("CompleteExamTarget")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        IStudentPlanReader reader,
        [FromQuery(Name = "includeArchived")] bool? includeArchived,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        var includeArch = includeArchived == true;
        var targets = await reader.ListTargetsAsync(studentId, includeArch, ct).ConfigureAwait(false);
        return Results.Ok(new ExamTargetListResponseDto(
            Items: targets.Select(ExamTargetResponseDto.From).ToArray(),
            IncludeArchived: includeArch));
    }

    private static async Task<IResult> AddAsync(
        [FromBody] AddExamTargetRequestDto? req,
        ClaimsPrincipal user,
        IStudentPlanCommandHandler handler,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!TryValidateAdd(req, out var err))
        {
            return BadRequest(err);
        }

        var cmd = new AddExamTargetCommand(
            StudentAnonId: studentId,
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(studentId),
            EnrollmentId: null,
            ExamCode: new ExamCode(req!.ExamCode!),
            Track: string.IsNullOrWhiteSpace(req.Track) ? null : new TrackCode(req.Track!),
            Sitting: new SittingCode(req.Sitting!.AcademicYear!, req.Sitting.Season, req.Sitting.Moed),
            WeeklyHours: req.WeeklyHours,
            ReasonTag: req.ReasonTag);

        var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            logger.LogInformation(
                "[EXAM_TARGETS] add rejected student={StudentId} error={Error}",
                studentId, result.Error);
            return ErrorFromResult(result);
        }

        logger.LogInformation(
            "[EXAM_TARGETS] add ok student={StudentId} target={TargetId}",
            studentId, result.TargetId);

        return Results.Created(
            $"/api/me/exam-targets/{result.TargetId!.Value.Value}",
            new ExamTargetResponseDto(
                Id: result.TargetId.Value.Value,
                Source: ExamTargetSource.Student,
                AssignedById: studentId,
                EnrollmentId: null,
                ExamCode: req.ExamCode!,
                Track: req.Track,
                Sitting: req.Sitting!,
                WeeklyHours: req.WeeklyHours,
                ReasonTag: req.ReasonTag,
                IsActive: true,
                ArchivedAt: null));
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        [FromBody] UpdateExamTargetRequestDto? req,
        ClaimsPrincipal user,
        IStudentPlanCommandHandler handler,
        IStudentPlanReader reader,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!TryValidateUpdate(req, out var err))
        {
            return BadRequest(err);
        }

        ExamTargetId targetId;
        try { targetId = new ExamTargetId(id); }
        catch (ArgumentException) { return BadRequest("id must be non-empty."); }

        var cmd = new UpdateExamTargetCommand(
            StudentAnonId: studentId,
            TargetId: targetId,
            Track: string.IsNullOrWhiteSpace(req!.Track) ? null : new TrackCode(req.Track!),
            Sitting: new SittingCode(req.Sitting!.AcademicYear!, req.Sitting.Season, req.Sitting.Moed),
            WeeklyHours: req.WeeklyHours,
            ReasonTag: req.ReasonTag);

        var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return ErrorFromResult(result);
        }

        var updated = await reader.FindTargetAsync(studentId, targetId, ct).ConfigureAwait(false);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(ExamTargetResponseDto.From(updated));
    }

    private static async Task<IResult> ArchiveAsync(
        string id,
        ClaimsPrincipal user,
        IStudentPlanCommandHandler handler,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        ExamTargetId targetId;
        try { targetId = new ExamTargetId(id); }
        catch (ArgumentException) { return BadRequest("id must be non-empty."); }

        var cmd = new ArchiveExamTargetCommand(studentId, targetId, ArchiveReason.StudentDeclined);
        var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
        return result.Success ? Results.NoContent() : ErrorFromResult(result);
    }

    private static async Task<IResult> CompleteAsync(
        string id,
        ClaimsPrincipal user,
        IStudentPlanCommandHandler handler,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        ExamTargetId targetId;
        try { targetId = new ExamTargetId(id); }
        catch (ArgumentException) { return BadRequest("id must be non-empty."); }

        var cmd = new CompleteExamTargetCommand(studentId, targetId);
        var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
        return result.Success ? Results.NoContent() : ErrorFromResult(result);
    }

    // ── Validation (exposed internal for unit tests) ─────────────────────

    internal static bool TryValidateAdd(AddExamTargetRequestDto? req, out string error)
    {
        error = "";
        if (req is null) { error = "Request body is required."; return false; }
        if (string.IsNullOrWhiteSpace(req.ExamCode)) { error = "examCode is required."; return false; }
        if (req.Sitting is null) { error = "sitting is required."; return false; }
        if (string.IsNullOrWhiteSpace(req.Sitting.AcademicYear))
        {
            error = "sitting.academicYear is required."; return false;
        }
        if (!Enum.IsDefined(typeof(SittingSeason), req.Sitting.Season))
        {
            error = "sitting.season invalid."; return false;
        }
        if (!Enum.IsDefined(typeof(SittingMoed), req.Sitting.Moed))
        {
            error = "sitting.moed invalid."; return false;
        }
        if (req.WeeklyHours < ExamTarget.MinWeeklyHours || req.WeeklyHours > ExamTarget.MaxWeeklyHours)
        {
            error = $"weeklyHours must be between {ExamTarget.MinWeeklyHours} and {ExamTarget.MaxWeeklyHours}.";
            return false;
        }
        if (req.ReasonTag is { } r && !Enum.IsDefined(typeof(ReasonTag), r))
        {
            error = "reasonTag invalid."; return false;
        }
        return true;
    }

    internal static bool TryValidateUpdate(UpdateExamTargetRequestDto? req, out string error)
    {
        error = "";
        if (req is null) { error = "Request body is required."; return false; }
        if (req.Sitting is null) { error = "sitting is required."; return false; }
        if (string.IsNullOrWhiteSpace(req.Sitting.AcademicYear))
        {
            error = "sitting.academicYear is required."; return false;
        }
        if (!Enum.IsDefined(typeof(SittingSeason), req.Sitting.Season))
        {
            error = "sitting.season invalid."; return false;
        }
        if (!Enum.IsDefined(typeof(SittingMoed), req.Sitting.Moed))
        {
            error = "sitting.moed invalid."; return false;
        }
        if (req.WeeklyHours < ExamTarget.MinWeeklyHours || req.WeeklyHours > ExamTarget.MaxWeeklyHours)
        {
            error = $"weeklyHours must be between {ExamTarget.MinWeeklyHours} and {ExamTarget.MaxWeeklyHours}.";
            return false;
        }
        if (req.ReasonTag is { } r && !Enum.IsDefined(typeof(ReasonTag), r))
        {
            error = "reasonTag invalid."; return false;
        }
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static IResult BadRequest(string message) =>
        Results.BadRequest(new CenaError(
            ErrorCodes.CENA_INTERNAL_VALIDATION,
            message,
            ErrorCategory.Validation,
            null, null));

    internal static IResult ErrorFromResult(CommandResult result)
    {
        var (status, code, msg) = MapError(result.Error!.Value);
        var err = new CenaError(code, msg, ErrorCategory.Validation, null, null);
        return status switch
        {
            StatusCodes.Status404NotFound => Results.NotFound(err),
            StatusCodes.Status409Conflict => Results.Conflict(err),
            _ => Results.BadRequest(err),
        };
    }

    internal static (int StatusCode, string ErrorCode, string Message) MapError(CommandError error)
        => error switch
        {
            CommandError.ActiveTargetCapExceeded =>
                (StatusCodes.Status409Conflict,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "Active target cap (5) reached; archive another target first."),
            CommandError.WeeklyBudgetExceeded =>
                (StatusCodes.Status409Conflict,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "Total weekly hours across active targets would exceed 40."),
            CommandError.DuplicateTarget =>
                (StatusCodes.Status409Conflict,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "An active target with the same exam, sitting and track already exists."),
            CommandError.TargetNotFound =>
                (StatusCodes.Status404NotFound,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "Exam target not found."),
            CommandError.TargetArchived =>
                (StatusCodes.Status400BadRequest,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "Archived targets cannot be mutated."),
            CommandError.WeeklyHoursOutOfRange =>
                (StatusCodes.Status400BadRequest,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "weeklyHours must be between 1 and 40."),
            CommandError.SourceAssignmentMismatch =>
                (StatusCodes.Status400BadRequest,
                 ErrorCodes.CENA_INTERNAL_VALIDATION,
                 "Source / enrollment mismatch."),
            _ =>
                (StatusCodes.Status400BadRequest,
                 ErrorCodes.CENA_INTERNAL_ERROR,
                 "Unexpected command error."),
        };

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

/// <summary>Sitting tuple as it travels on the wire.</summary>
public sealed record SittingCodeDto(
    string? AcademicYear,
    SittingSeason Season,
    SittingMoed Moed);

/// <summary>POST body for adding a target.</summary>
public sealed record AddExamTargetRequestDto(
    string? ExamCode,
    string? Track,
    SittingCodeDto? Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag);

/// <summary>PUT body for updating a target.</summary>
public sealed record UpdateExamTargetRequestDto(
    string? Track,
    SittingCodeDto? Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag);

/// <summary>Per-target response shape.</summary>
public sealed record ExamTargetResponseDto(
    string Id,
    ExamTargetSource Source,
    string AssignedById,
    string? EnrollmentId,
    string ExamCode,
    string? Track,
    SittingCodeDto Sitting,
    int WeeklyHours,
    ReasonTag? ReasonTag,
    bool IsActive,
    DateTimeOffset? ArchivedAt)
{
    /// <summary>Project an aggregate target onto the wire DTO.</summary>
    public static ExamTargetResponseDto From(ExamTarget t) => new(
        Id: t.Id.Value,
        Source: t.Source,
        AssignedById: t.AssignedById.Value,
        EnrollmentId: t.EnrollmentId?.Value,
        ExamCode: t.ExamCode.Value,
        Track: t.Track?.Value,
        Sitting: new SittingCodeDto(t.Sitting.AcademicYear, t.Sitting.Season, t.Sitting.Moed),
        WeeklyHours: t.WeeklyHours,
        ReasonTag: t.ReasonTag,
        IsActive: t.IsActive,
        ArchivedAt: t.ArchivedAt);
}

/// <summary>List response shape.</summary>
public sealed record ExamTargetListResponseDto(
    IReadOnlyList<ExamTargetResponseDto> Items,
    bool IncludeArchived);
