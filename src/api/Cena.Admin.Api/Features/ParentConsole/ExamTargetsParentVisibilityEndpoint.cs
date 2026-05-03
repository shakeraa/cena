// =============================================================================
// Cena Platform — Parent Console: Exam-Targets Visibility Endpoint (prr-230)
//
// Surfaces the list of a linked minor's exam targets that the PARENT is
// allowed to see. The aggregate filter lives HERE — at the read surface —
// so the student's plan aggregate stays a single source of truth for
// target state and only the parent view is narrowed by the
// ParentVisibility flag.
//
//   GET /api/v1/parent/minors/{studentAnonId}/exam-targets
//
// Response shape mirrors /api/me/exam-targets except it omits admin/
// classroom-only fields (AssignedById, EnrollmentId) and applies the
// parent-visibility filter:
//
//   * Only targets with ParentVisibility == Visible are returned.
//   * SafetyFlag-tagged targets are ALWAYS returned regardless of the
//     stored visibility flag — duty-of-care carve-out mirrors ADR-0041.
//   * Archived targets are NOT returned by default (reuses the student
//     endpoint's default).
//
// Authorization:
//   - Caller role must be PARENT.
//   - ParentAuthorizationGuard.AssertCanAccessAsync enforces
//     (parentActorId, studentAnonId, instituteId) binding per ADR-0001
//     tenant scoping. Any mismatch → 403.
//
// Age-band sourcing:
//   IStudentAgeBandLookup reads authoritative DOB. A request parameter
//   for band is NOT accepted.
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.StudentPlan;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Security;
using Cena.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>
/// Sitting tuple on the parent-facing wire. Mirrors the student-facing
/// shape but uses string enums for wire stability across admin+student
/// hosts.
/// </summary>
public sealed record ParentSittingCodeDto(
    string AcademicYear,
    string Season,
    string Moed);

/// <summary>One target row in the parent view.</summary>
public sealed record ParentExamTargetDto(
    string Id,
    string ExamCode,
    string? Track,
    ParentSittingCodeDto Sitting,
    int WeeklyHours,
    string? ReasonTag,
    bool IsActive,
    IReadOnlyList<string> QuestionPaperCodes,
    DateTimeOffset CreatedAt);

/// <summary>Response envelope.</summary>
public sealed record ParentExamTargetsResponseDto(
    string StudentAnonId,
    string SubjectBand,
    IReadOnlyList<ParentExamTargetDto> Targets);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Maps <c>GET /api/v1/parent/minors/{studentAnonId}/exam-targets</c>.
/// </summary>
public static class ExamTargetsParentVisibilityEndpoint
{
    /// <summary>Canonical route path.</summary>
    public const string Route = "/api/v1/parent/minors/{studentAnonId}/exam-targets";

    internal sealed class ExamTargetsParentVisibilityMarker { }

    /// <summary>Register the route.</summary>
    public static IEndpointRouteBuilder MapExamTargetsParentVisibilityEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetMinorExamTargets")
            .WithTags("Parent Console", "Exam Targets")
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> HandleGetAsync(
        string studentAnonId,
        HttpContext http,
        IStudentAgeBandLookup bandLookup,
        IStudentPlanReader planReader,
        ILogger<ExamTargetsParentVisibilityMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        }

        // Role gate: PARENT-only (admins use the admin exam-targets route,
        // which is out of scope for PRR-230).
        if (!http.User.IsInRole("PARENT"))
        {
            return Results.Forbid();
        }

        // ADR-0001 tenant scoping via the binding guard. Any cross-tenant
        // attempt raises ForbiddenException which becomes 403.
        ParentChildBindingResolution binding;
        try
        {
            binding = await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
        }
        catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
        {
            return Results.Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var band = await bandLookup.ResolveBandAsync(studentAnonId, now, ct).ConfigureAwait(false);
        if (band is null)
        {
            logger.LogWarning(
                "[prr-230] exam-targets refused: no DOB on profile student={StudentId}",
                studentAnonId);
            return Results.Forbid();
        }

        // Load ACTIVE targets (parents don't care about archived history on
        // this surface — there's no retire/restore ceremony here).
        var all = await planReader.ListTargetsAsync(studentAnonId, includeArchived: false, ct)
            .ConfigureAwait(false);

        // PRR-230 visibility filter: Visible OR SafetyFlag carve-out.
        // (Archived already excluded by ListTargetsAsync default.)
        var visible = all
            .Where(t => t.ParentVisibility == ParentVisibility.Visible || t.IsSafetyFlagged)
            .ToList();

        logger.LogInformation(
            "[prr-230] exam-targets resolved parent={ParentId} student={StudentId} "
                + "institute={InstituteId} band={Band} total={Total} visible={Visible}",
            binding.ParentActorId,
            studentAnonId,
            binding.InstituteId,
            band.Value,
            all.Count,
            visible.Count);

        var dto = new ParentExamTargetsResponseDto(
            StudentAnonId: studentAnonId,
            SubjectBand: band.Value.ToString(),
            Targets: visible.Select(Project).ToList());

        return Results.Ok(dto);
    }

    private static ParentExamTargetDto Project(ExamTarget t) =>
        new(
            Id: t.Id.Value,
            ExamCode: t.ExamCode.Value,
            Track: t.Track?.Value,
            Sitting: new ParentSittingCodeDto(
                AcademicYear: t.Sitting.AcademicYear,
                Season: t.Sitting.Season.ToString(),
                Moed: t.Sitting.Moed.ToString()),
            WeeklyHours: t.WeeklyHours,
            ReasonTag: t.ReasonTag?.ToString(),
            IsActive: t.IsActive,
            QuestionPaperCodes: t.QuestionPaperCodes,
            CreatedAt: t.CreatedAt);

    // -- AuthZ helper, mirroring DashboardVisibilityEndpoint --

    internal static async Task<ParentChildBindingResolution> RequireParentBindingAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var bindingService = services.GetRequiredService<IParentChildBindingService>();
        var logger = services.GetRequiredService<ILogger<ExamTargetsParentVisibilityMarker>>();
        var instituteId = ResolveInstituteId(http);
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                "PARENT caller is missing a required institute_id claim.");
        }
        return await ParentAuthorizationGuard.AssertCanAccessAsync(
            http.User, studentAnonId, instituteId, bindingService, logger, ct)
            .ConfigureAwait(false);
    }

    private static string ResolveInstituteId(HttpContext http)
    {
        var claims = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        return claims.Count == 0 ? string.Empty : claims[0];
    }
}
