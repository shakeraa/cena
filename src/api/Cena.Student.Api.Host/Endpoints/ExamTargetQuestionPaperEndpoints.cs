// =============================================================================
// Cena Platform — /api/me/exam-targets/{id}/question-papers + /per-paper-sitting
// endpoints (PRR-243, ADR-0050 §1)
//
// Post-hoc שאלון management for Bagrut targets. Keeps the two endpoints
// (paper add/remove and per-paper sitting override set/clear) in one
// partial file separate from the core CRUD endpoints so neither file
// breaches the 500-LOC cap.
//
// Wire shape:
//
//   PATCH /api/me/exam-targets/{id}/question-papers
//        body: { "add"?: string, "remove"?: string,
//                "sittingOverride"?: SittingCodeDto? }
//     200 OK + ExamTargetResponseDto  — post-mutation target
//     400/404/409 + CenaError
//
//   PATCH /api/me/exam-targets/{id}/per-paper-sitting
//        body: { "paperCode": string, "sitting": SittingCodeDto | null }
//     (sitting:null clears the override)
//     200 OK + ExamTargetResponseDto
//
// Auth: student-owned via student_id JWT claim. Rate-limited via the
// shared "api" policy.
//
// Invariants enforced server-side by StudentPlanCommandHandler
// (PRR-243 partial) — the endpoint layer only shape-validates.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.StudentPlan;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Minimal-API endpoints for the שאלון post-hoc surface.</summary>
public static class ExamTargetQuestionPaperEndpoints
{
    private sealed class LoggerMarker { }

    /// <summary>
    /// Register the שאלון endpoints under <c>/api/me/exam-targets/{id}</c>.
    /// Call this AFTER <see cref="ExamTargetEndpoints.MapExamTargetEndpoints"/>
    /// from the composition root.
    /// </summary>
    public static IEndpointRouteBuilder MapExamTargetQuestionPaperEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/exam-targets")
            .WithTags(ExamTargetEndpoints.GroupTag)
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapPatch("{id}/question-papers", PatchQuestionPapersAsync)
            .WithName("PatchExamTargetQuestionPapers")
            .Produces<ExamTargetResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        group.MapPatch("{id}/per-paper-sitting", PatchPerPaperSittingAsync)
            .WithName("PatchExamTargetPerPaperSitting")
            .Produces<ExamTargetResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        return app;
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> PatchQuestionPapersAsync(
        string id,
        [FromBody] PatchQuestionPapersRequestDto? req,
        ClaimsPrincipal user,
        IStudentPlanCommandHandler handler,
        IStudentPlanReader reader,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!TryValidatePatchQuestionPapers(req, out var err))
        {
            return BadRequest(err);
        }

        ExamTargetId targetId;
        try { targetId = new ExamTargetId(id); }
        catch (ArgumentException) { return BadRequest("id must be non-empty."); }

        // Exactly one of add/remove must be supplied (guarded by the
        // validator). Dispatch to the matching command.
        CommandResult result;
        if (!string.IsNullOrWhiteSpace(req!.Add))
        {
            SittingCode? sittingOverride = null;
            if (req.SittingOverride is { } so)
            {
                sittingOverride = new SittingCode(so.AcademicYear!, so.Season, so.Moed);
            }
            result = await handler.HandleAsync(
                new AddQuestionPaperCommand(studentId, targetId, req.Add!.Trim(), sittingOverride),
                ct).ConfigureAwait(false);
        }
        else
        {
            result = await handler.HandleAsync(
                new RemoveQuestionPaperCommand(studentId, targetId, req.Remove!.Trim()),
                ct).ConfigureAwait(false);
        }

        if (!result.Success)
        {
            logger.LogInformation(
                "[EXAM_TARGETS][PAPERS] patch rejected student={StudentId} target={TargetId} error={Error}",
                studentId, targetId.Value, result.Error);
            return ExamTargetEndpoints.ErrorFromResult(result);
        }

        var updated = await reader.FindTargetAsync(studentId, targetId, ct).ConfigureAwait(false);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(ExamTargetResponseDto.From(updated));
    }

    private static async Task<IResult> PatchPerPaperSittingAsync(
        string id,
        [FromBody] PatchPerPaperSittingRequestDto? req,
        ClaimsPrincipal user,
        IStudentPlanCommandHandler handler,
        IStudentPlanReader reader,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!TryValidatePatchPerPaperSitting(req, out var err))
        {
            return BadRequest(err);
        }

        ExamTargetId targetId;
        try { targetId = new ExamTargetId(id); }
        catch (ArgumentException) { return BadRequest("id must be non-empty."); }

        CommandResult result;
        if (req!.Sitting is { } s)
        {
            result = await handler.HandleAsync(
                new SetPerPaperSittingOverrideCommand(
                    studentId, targetId, req.PaperCode!.Trim(),
                    new SittingCode(s.AcademicYear!, s.Season, s.Moed)),
                ct).ConfigureAwait(false);
        }
        else
        {
            // Null sitting = clear override.
            result = await handler.HandleAsync(
                new ClearPerPaperSittingOverrideCommand(studentId, targetId, req.PaperCode!.Trim()),
                ct).ConfigureAwait(false);
        }

        if (!result.Success)
        {
            logger.LogInformation(
                "[EXAM_TARGETS][PAPER_SITTING] patch rejected student={StudentId} target={TargetId} error={Error}",
                studentId, targetId.Value, result.Error);
            return ExamTargetEndpoints.ErrorFromResult(result);
        }

        var updated = await reader.FindTargetAsync(studentId, targetId, ct).ConfigureAwait(false);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(ExamTargetResponseDto.From(updated));
    }

    // ── Shape validation (internal for tests) ─────────────────────────────

    internal static bool TryValidatePatchQuestionPapers(PatchQuestionPapersRequestDto? req, out string error)
    {
        error = "";
        if (req is null) { error = "Request body is required."; return false; }

        var hasAdd = !string.IsNullOrWhiteSpace(req.Add);
        var hasRemove = !string.IsNullOrWhiteSpace(req.Remove);
        if (hasAdd == hasRemove)
        {
            // Both supplied OR both empty — ambiguous; reject.
            error = "Exactly one of 'add' or 'remove' must be supplied.";
            return false;
        }

        if (hasRemove && req.SittingOverride is not null)
        {
            error = "sittingOverride only applies when adding a paper.";
            return false;
        }

        if (req.SittingOverride is { } so)
        {
            if (string.IsNullOrWhiteSpace(so.AcademicYear))
            {
                error = "sittingOverride.academicYear is required."; return false;
            }
            if (!Enum.IsDefined(typeof(SittingSeason), so.Season))
            {
                error = "sittingOverride.season invalid."; return false;
            }
            if (!Enum.IsDefined(typeof(SittingMoed), so.Moed))
            {
                error = "sittingOverride.moed invalid."; return false;
            }
        }

        return true;
    }

    internal static bool TryValidatePatchPerPaperSitting(PatchPerPaperSittingRequestDto? req, out string error)
    {
        error = "";
        if (req is null) { error = "Request body is required."; return false; }
        if (string.IsNullOrWhiteSpace(req.PaperCode))
        {
            error = "paperCode is required."; return false;
        }

        // sitting:null is valid (clear path). If supplied, shape-check.
        if (req.Sitting is { } s)
        {
            if (string.IsNullOrWhiteSpace(s.AcademicYear))
            {
                error = "sitting.academicYear is required."; return false;
            }
            if (!Enum.IsDefined(typeof(SittingSeason), s.Season))
            {
                error = "sitting.season invalid."; return false;
            }
            if (!Enum.IsDefined(typeof(SittingMoed), s.Moed))
            {
                error = "sitting.moed invalid."; return false;
            }
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

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

/// <summary>
/// PATCH body for the question-paper mutation endpoint. Exactly one of
/// <see cref="Add"/> / <see cref="Remove"/> must be supplied.
/// <see cref="SittingOverride"/> only applies to adds.
/// </summary>
public sealed record PatchQuestionPapersRequestDto(
    string? Add,
    string? Remove,
    SittingCodeDto? SittingOverride);

/// <summary>
/// PATCH body for the per-שאלון sitting override endpoint.
/// <see cref="Sitting"/> null clears the override.
/// </summary>
public sealed record PatchPerPaperSittingRequestDto(
    string? PaperCode,
    SittingCodeDto? Sitting);
