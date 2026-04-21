// =============================================================================
// Cena Platform — /api/me/exam-targets/{id}/visibility endpoint (prr-230)
//
// Student-facing toggle for per-target parent-dashboard visibility:
//
//   POST /api/me/exam-targets/{id}/visibility
//      Body: { "visibility": "Visible" | "Hidden", "reason"?: "<free>" }
//
// Authority model (applied HERE — aggregate layer does not branch on band):
//
//   Under13    → student CANNOT toggle. COPPA: parent governs. 403.
//   Teen13to15 → student CAN toggle (both directions). Transparency band —
//                student may opt IN or out (ADR-0041 transparency + minor
//                autonomy over non-safety purposes).
//   Teen16to17 → student CAN toggle both directions.
//   Adult      → student CAN toggle (purely self-governance).
//
// Safety-flag carve-out: enforced at the aggregate (SetParentVisibility
// handler returns ParentVisibilitySafetyFlagLocked → 409).
//
// Age-band sourcing:
//   Via IStudentAgeBandLookup — authoritative DOB on profile. Request
//   body may NOT carry band or age. When the profile has no DOB the
//   endpoint 403s rather than assuming a band.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Consent;
using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Body for POST .../{id}/visibility.</summary>
public sealed record SetParentVisibilityRequestDto(
    string? Visibility,
    string? Reason);

/// <summary>Response for POST .../{id}/visibility.</summary>
public sealed record SetParentVisibilityResponseDto(
    string Id,
    ParentVisibility Visibility);

/// <summary>
/// Minimal-API endpoint for PRR-230 parent-visibility toggle.
/// </summary>
public static class ExamTargetParentVisibilityEndpoints
{
    private sealed class LoggerMarker { }

    /// <summary>Canonical route path.</summary>
    public const string VisibilityRoute = "/api/me/exam-targets/{id}/visibility";

    /// <summary>Register the endpoint.</summary>
    public static IEndpointRouteBuilder MapExamTargetParentVisibilityEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(VisibilityRoute, HandleAsync)
            .WithName("SetExamTargetParentVisibility")
            .WithTags(ExamTargetEndpoints.GroupTag, "Parent Visibility")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .Produces<SetParentVisibilityResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string id,
        [FromBody] SetParentVisibilityRequestDto? req,
        ClaimsPrincipal user,
        IStudentAgeBandLookup bandLookup,
        IStudentPlanCommandHandler handler,
        IStudentPlanReader reader,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = user.FindFirstValue("student_id")
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue("user_id")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        if (req is null || string.IsNullOrWhiteSpace(req.Visibility))
        {
            return BadRequest("visibility is required.");
        }
        if (!Enum.TryParse<ParentVisibility>(req.Visibility, ignoreCase: false, out var requested)
            || !Enum.IsDefined(typeof(ParentVisibility), requested))
        {
            return BadRequest($"visibility must be one of: Visible, Hidden.");
        }

        ExamTargetId targetId;
        try { targetId = new ExamTargetId(id); }
        catch (ArgumentException) { return BadRequest("id must be non-empty."); }

        // Authoritative age-band lookup — Under13 has no authority to toggle.
        var now = DateTimeOffset.UtcNow;
        var band = await bandLookup.ResolveBandAsync(studentId, now, ct).ConfigureAwait(false);
        if (band is null)
        {
            logger.LogWarning(
                "[prr-230] parent-visibility refused: no DOB on profile student={StudentId}",
                studentId);
            return Results.Forbid();
        }
        if (band.Value == AgeBand.Under13)
        {
            logger.LogInformation(
                "[prr-230] parent-visibility refused: Under13 may not self-toggle student={StudentId}",
                studentId);
            return Results.Forbid();
        }

        var cmd = new SetParentVisibilityCommand(
            StudentAnonId: studentId,
            TargetId: targetId,
            Visibility: requested,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: studentId,
            Reason: req.Reason ?? "student-self-toggle");

        var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return ExamTargetEndpoints.ErrorFromResult(result);
        }

        // Return the current target with its post-command visibility.
        var updated = await reader.FindTargetAsync(studentId, targetId, ct).ConfigureAwait(false);
        if (updated is null) return Results.NotFound();

        logger.LogInformation(
            "[prr-230] visibility changed student={StudentId} target={TargetId} visibility={Visibility} band={Band}",
            studentId, targetId, requested, band.Value);

        return Results.Ok(new SetParentVisibilityResponseDto(
            Id: updated.Id.Value,
            Visibility: updated.ParentVisibility));
    }

    private static IResult BadRequest(string message) =>
        Results.BadRequest(new CenaError(
            ErrorCodes.CENA_INTERNAL_VALIDATION,
            message,
            ErrorCategory.Validation,
            null, null));
}
