// =============================================================================
// Cena Platform — /api/me/exam-targets/{id}/extend-retention (prr-229)
//
// POST /api/me/exam-targets/{examTargetCode}/extend-retention
//     → Opt the authenticated student in to the 60-month retention
//       extension for their archived exam targets.
// DELETE /api/me/exam-targets/{examTargetCode}/extend-retention
//     → Clear the opt-in; default 24-month retention resumes.
// GET /api/me/exam-targets/retention
//     → Read the current opt-in state + computed horizon for the caller.
//
// The {examTargetCode} path segment is ACCEPTED for UX alignment (student
// clicks "extend retention" from a specific archived target card) but
// the server stores the opt-in at the PROFILE level per ADR-0050 §6 —
// extending retention for one archived target extends it for every
// archived target belonging to that student. The path parameter is
// validated (rejects malformed codes) so misuse fails closed.
//
// Audit: every opt-in / clear logs a [SIEM] line. No PII in the log line.
// Rate limit: standard "api" policy.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.ExamTargets;
using Cena.Actors.Infrastructure;
using Cena.Actors.Retention;
using Cena.Infrastructure.Compliance.KeyStore;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>
/// Minimal-API endpoints for the prr-229 retention-extension surface.
/// </summary>
public static class ExamTargetRetentionEndpoints
{
    private sealed class LoggerMarker { }

    /// <summary>Map the endpoints into the given route builder.</summary>
    public static IEndpointRouteBuilder MapExamTargetRetentionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/exam-targets")
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapPost("{examTargetCode}/extend-retention", ExtendAsync)
            .WithName("ExtendExamTargetRetention")
            .Produces<RetentionExtensionResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest);

        group.MapDelete("{examTargetCode}/extend-retention", ClearAsync)
            .WithName("ClearExamTargetRetentionExtension")
            .Produces<RetentionExtensionResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest);

        group.MapGet("retention", GetAsync)
            .WithName("GetExamTargetRetentionExtension")
            .Produces<RetentionExtensionResponseDto>(StatusCodes.Status200OK);

        return app;
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> ExtendAsync(
        string examTargetCode,
        ClaimsPrincipal user,
        IExamTargetRetentionExtensionStore store,
        IClock clock,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!ExamTargetCode.TryParse(examTargetCode, out _))
        {
            return Results.BadRequest(new CenaError(
                ErrorCodes.CENA_INTERNAL_VALIDATION,
                "Invalid exam target code.",
                ErrorCategory.Validation, null, null));
        }

        var now = clock.UtcNow;
        var horizon = now.AddMonths(
            ExamTargetRetentionPolicy.MaxExtendedRetentionMonths);

        var ext = new ExamTargetRetentionExtension(
            StudentAnonId: studentId,
            SetAtUtc: now,
            ExtendedUntilUtc: horizon);
        await store.SetAsync(ext, ct).ConfigureAwait(false);

        logger.LogInformation(
            "[SIEM] ExamTargetRetentionExtended: student={StudentIdHash} "
            + "target={Target} extendedUntil={ExtendedUntilUtc}",
            InMemorySubjectKeyStore.HashSubjectForLog(studentId),
            examTargetCode,
            horizon);

        return Results.Ok(new RetentionExtensionResponseDto(
            Extended: true,
            ExtendedUntilUtc: horizon,
            DefaultRetentionMonths:
                ExamTargetRetentionPolicy.DefaultRetentionMonths,
            MaxExtendedRetentionMonths:
                ExamTargetRetentionPolicy.MaxExtendedRetentionMonths));
    }

    private static async Task<IResult> ClearAsync(
        string examTargetCode,
        ClaimsPrincipal user,
        IExamTargetRetentionExtensionStore store,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (!ExamTargetCode.TryParse(examTargetCode, out _))
        {
            return Results.BadRequest(new CenaError(
                ErrorCodes.CENA_INTERNAL_VALIDATION,
                "Invalid exam target code.",
                ErrorCategory.Validation, null, null));
        }

        var removed = await store.DeleteAsync(studentId, ct).ConfigureAwait(false);

        logger.LogInformation(
            "[SIEM] ExamTargetRetentionExtensionCleared: student={StudentIdHash} "
            + "target={Target} removed={Removed}",
            InMemorySubjectKeyStore.HashSubjectForLog(studentId),
            examTargetCode, removed);

        return Results.Ok(new RetentionExtensionResponseDto(
            Extended: false,
            ExtendedUntilUtc: null,
            DefaultRetentionMonths:
                ExamTargetRetentionPolicy.DefaultRetentionMonths,
            MaxExtendedRetentionMonths:
                ExamTargetRetentionPolicy.MaxExtendedRetentionMonths));
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IExamTargetRetentionExtensionStore store,
        IClock clock,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        var row = await store.TryGetAsync(studentId, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var active = row is not null && row.ExtendedUntilUtc > now;

        return Results.Ok(new RetentionExtensionResponseDto(
            Extended: active,
            ExtendedUntilUtc: active ? row!.ExtendedUntilUtc : null,
            DefaultRetentionMonths:
                ExamTargetRetentionPolicy.DefaultRetentionMonths,
            MaxExtendedRetentionMonths:
                ExamTargetRetentionPolicy.MaxExtendedRetentionMonths));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirstValue("student_id")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}

/// <summary>
/// Response DTO for the retention extension surface.
/// </summary>
public sealed record RetentionExtensionResponseDto(
    bool Extended,
    DateTimeOffset? ExtendedUntilUtc,
    int DefaultRetentionMonths,
    int MaxExtendedRetentionMonths);
