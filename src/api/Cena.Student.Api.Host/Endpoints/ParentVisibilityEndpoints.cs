// =============================================================================
// Cena Platform — Student-facing parent-visibility endpoints (prr-052)
//
// Routes under /api/v1/students/me/parent-visibility/:
//
//   GET  /api/v1/students/me/parent-visibility
//          Returns the age-band-filtered dashboard the STUDENT sees,
//          mirroring what their parent sees (per ADR-0041 transparency
//          right at 13+; refused for Under13).
//
//   POST /api/v1/students/me/parent-visibility/revoke-purpose
//          Body: { "purpose": "ParentDigest", "reason": "..." }
//          Permitted only for Teen16to17 and Adult bands (PPA minor-
//          dignity / adult self-determination). Refuses safety-category
//          keys with 403 (duty-of-care).
//
//   POST /api/v1/students/me/parent-visibility/restore-purpose
//          Body: { "purpose": "ParentDigest" }
//          Symmetric restore.
//
// AuthZ:
//   Caller role must be STUDENT. The endpoint resolves the student id
//   from the session claim — NEVER from a request parameter — so there
//   is no spoofing surface.
//
// Age-band sourcing:
//   Via IStudentAgeBandLookup (authoritative DOB on profile). A request
//   body carrying `band` or `age` is IGNORED. If the profile has no
//   DOB yet, the endpoint 403s rather than assuming Adult.
//
// Safety-category protection:
//   AgeBandPolicy.IsSafetyCategoryKey catches any attempt to veto a
//   safety flag. Safety keys never appear in the revoke surface; the
//   check is belt-and-braces.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>
/// One row in the "your parent sees this" student-facing dashboard.
/// </summary>
public sealed record StudentVisibilityFieldDto(
    string FieldKey,
    string Kind,
    bool ParentCanSee,
    bool StudentCanVeto,
    string LegalBasisRef);

/// <summary>
/// GET /api/v1/students/me/parent-visibility response.
/// </summary>
public sealed record StudentVisibilityDto(
    string StudentAnonId,
    string SubjectBand,
    bool StudentCanSeeParentView,
    bool StudentHasAnyVetoRight,
    IReadOnlyList<StudentVisibilityFieldDto> Fields);

/// <summary>Body of the revoke-purpose POST.</summary>
public sealed record RevokeVisibilityPurposeRequest(
    string Purpose,
    string? Reason);

/// <summary>Body of the restore-purpose POST.</summary>
public sealed record RestoreVisibilityPurposeRequest(
    string Purpose);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Maps student-facing <c>/api/v1/students/me/parent-visibility/*</c> routes.
/// </summary>
public static class ParentVisibilityEndpoints
{
    /// <summary>Canonical GET dashboard route.</summary>
    public const string GetRoute = "/api/v1/students/me/parent-visibility";

    /// <summary>Canonical revoke-purpose POST route.</summary>
    public const string RevokeRoute = "/api/v1/students/me/parent-visibility/revoke-purpose";

    /// <summary>Canonical restore-purpose POST route.</summary>
    public const string RestoreRoute = "/api/v1/students/me/parent-visibility/restore-purpose";

    internal sealed class ParentVisibilityEndpointMarker { }

    /// <summary>Register the routes.</summary>
    public static IEndpointRouteBuilder MapParentVisibilityEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(GetRoute, HandleGetAsync)
            .WithName("GetMyParentVisibility")
            .WithTags("Student Privacy", "Parent Visibility")
            .RequireAuthorization();

        app.MapPost(RevokeRoute, HandleRevokeAsync)
            .WithName("RevokeMyParentVisibilityPurpose")
            .WithTags("Student Privacy", "Parent Visibility")
            .RequireAuthorization();

        app.MapPost(RestoreRoute, HandleRestoreAsync)
            .WithName("RestoreMyParentVisibilityPurpose")
            .WithTags("Student Privacy", "Parent Visibility")
            .RequireAuthorization();

        return app;
    }

    // ── GET /parent-visibility ──────────────────────────────────────────

    private static async Task<IResult> HandleGetAsync(
        HttpContext http,
        IStudentAgeBandLookup bandLookup,
        IConsentAggregateStore consentStore,
        ILogger<ParentVisibilityEndpointMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(http.User);
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Results.Unauthorized();
        }

        if (!http.User.IsInRole("STUDENT") && http.User.FindFirstValue(ClaimTypes.Role) is not "STUDENT")
        {
            // The student-only gate is explicit: admins use the admin console;
            // parents use the parent-console endpoint under /api/v1/parent/.
            return Results.Forbid();
        }

        // IDOR: caller can only read their own visibility.
        ResourceOwnershipGuard.VerifyStudentAccess(http.User, studentId);

        var now = DateTimeOffset.UtcNow;
        var band = await bandLookup.ResolveBandAsync(studentId, now, ct).ConfigureAwait(false);
        if (band is null)
        {
            logger.LogWarning(
                "[prr-052] parent-visibility refused: no DOB on profile for student={StudentId}",
                studentId);
            return Results.Forbid();
        }

        // ADR-0041 Under13: no transparency right — the student dashboard
        // has nothing to show (parent-only dashboard). Return 403 rather
        // than a misleading empty list.
        if (!AgeBandPolicy.StudentSeesParentView(band.Value))
        {
            logger.LogInformation(
                "[prr-052] parent-visibility refused: band={Band} has no transparency right (Under13)",
                band.Value);
            return Results.Forbid();
        }

        var aggregate = await consentStore.LoadAsync(studentId, ct).ConfigureAwait(false);
        var vetoed = aggregate.State.VetoedParentVisibilityPurposes;
        var instituteId = ResolveInstituteId(http);

        var policyOutput = AgeBandPolicy.EvaluateDashboard(new VisibilityPolicyInput(
            SubjectBand: band.Value,
            VetoedPurposes: vetoed,
            InstituteId: instituteId,
            InstitutePolicyAllowsVeto: true));

        var dto = new StudentVisibilityDto(
            StudentAnonId: studentId,
            SubjectBand: band.Value.ToString(),
            StudentCanSeeParentView: policyOutput.StudentCanSeeParentView,
            StudentHasAnyVetoRight: policyOutput.StudentHasAnyVetoRight,
            Fields: policyOutput.Fields
                .Select(f => new StudentVisibilityFieldDto(
                    FieldKey: f.FieldKey,
                    Kind: f.Kind.ToString(),
                    ParentCanSee: f.ParentCanSee,
                    StudentCanVeto: f.StudentCanVeto,
                    LegalBasisRef: f.LegalBasisRef))
                .ToList());

        return Results.Ok(dto);
    }

    // ── POST /parent-visibility/revoke-purpose ───────────────────────────

    private static async Task<IResult> HandleRevokeAsync(
        RevokeVisibilityPurposeRequest request,
        HttpContext http,
        IStudentAgeBandLookup bandLookup,
        IConsentAggregateStore consentStore,
        EncryptedFieldAccessor piiAccessor,
        ILogger<ParentVisibilityEndpointMarker> logger,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Purpose))
        {
            return Results.BadRequest(new { error = "missing-purpose" });
        }

        var studentId = GetStudentId(http.User);
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Results.Unauthorized();
        }
        if (http.User.FindFirstValue(ClaimTypes.Role) != "STUDENT"
            && !http.User.IsInRole("STUDENT"))
        {
            return Results.Forbid();
        }
        ResourceOwnershipGuard.VerifyStudentAccess(http.User, studentId);

        // -- Safety-category guard BEFORE any band lookup: a safety key must
        //    be refused even if the student is Adult, because duty-of-care
        //    flags are not vetoable (ADR-0041 "minor-dignity" opinion §4b
        //    carves out duty-of-care).
        if (AgeBandPolicy.IsSafetyCategoryKey(request.Purpose, out var safetyCategory))
        {
            logger.LogWarning(
                "[prr-052] safety-category veto refused: student={StudentId} purpose={Purpose} category={Cat}",
                studentId, request.Purpose, safetyCategory);
            return Results.Forbid();
        }

        if (!Enum.TryParse<ConsentPurpose>(request.Purpose, ignoreCase: false, out var purpose)
            || !Enum.IsDefined(typeof(ConsentPurpose), purpose))
        {
            return Results.BadRequest(new { error = "unknown-purpose", purpose = request.Purpose });
        }

        // -- Authoritative age band.
        var now = DateTimeOffset.UtcNow;
        var band = await bandLookup.ResolveBandAsync(studentId, now, ct).ConfigureAwait(false);
        if (band is null)
        {
            return Results.Forbid();
        }

        if (!AgeBandPolicy.CanStudentVetoPurpose(band.Value, purpose))
        {
            logger.LogInformation(
                "[prr-052] veto refused: student={StudentId} band={Band} purpose={Purpose} — no authority",
                studentId, band.Value, purpose);
            return Results.Forbid();
        }

        var instituteId = ResolveInstituteId(http);
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            // Without institute scope we cannot audit tenant-correctly.
            return Results.Forbid();
        }

        // -- Build and persist the command via the command handler so PII
        //    encryption runs in exactly one place.
        var handler = new ConsentCommandHandler(piiAccessor);
        try
        {
            var evt = await handler.HandleAsync(new VetoParentVisibility(
                StudentSubjectId: studentId,
                StudentBand: band.Value,
                Purpose: purpose,
                Initiator: VetoInitiator.Student,
                InitiatorActorId: studentId,
                InstituteId: instituteId,
                VetoedAt: now,
                Reason: request.Reason ?? "student-self-veto"), ct).ConfigureAwait(false);

            await consentStore.AppendAsync(studentId, evt, ct).ConfigureAwait(false);
        }
        catch (ConsentAuthorizationException ex)
        {
            logger.LogWarning(
                "[prr-052] veto denied by aggregate: student={StudentId} purpose={Purpose} reason={Reason}",
                studentId, purpose, ex.Message);
            return Results.Forbid();
        }

        logger.LogInformation(
            "[prr-052] veto recorded: student={StudentId} band={Band} purpose={Purpose} institute={InstituteId}",
            studentId, band.Value, purpose, instituteId);

        return Results.Ok(new { Revoked = true, Purpose = purpose.ToString() });
    }

    // ── POST /parent-visibility/restore-purpose ──────────────────────────

    private static async Task<IResult> HandleRestoreAsync(
        RestoreVisibilityPurposeRequest request,
        HttpContext http,
        IStudentAgeBandLookup bandLookup,
        IConsentAggregateStore consentStore,
        EncryptedFieldAccessor piiAccessor,
        ILogger<ParentVisibilityEndpointMarker> logger,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Purpose))
        {
            return Results.BadRequest(new { error = "missing-purpose" });
        }

        var studentId = GetStudentId(http.User);
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Results.Unauthorized();
        }
        if (http.User.FindFirstValue(ClaimTypes.Role) != "STUDENT"
            && !http.User.IsInRole("STUDENT"))
        {
            return Results.Forbid();
        }
        ResourceOwnershipGuard.VerifyStudentAccess(http.User, studentId);

        if (AgeBandPolicy.IsSafetyCategoryKey(request.Purpose, out _))
        {
            // Safety flags are not vetoable → not restorable either.
            return Results.BadRequest(new { error = "not-a-purpose", note = "safety categories are not vetoable" });
        }

        if (!Enum.TryParse<ConsentPurpose>(request.Purpose, ignoreCase: false, out var purpose)
            || !Enum.IsDefined(typeof(ConsentPurpose), purpose))
        {
            return Results.BadRequest(new { error = "unknown-purpose", purpose = request.Purpose });
        }

        var now = DateTimeOffset.UtcNow;
        var band = await bandLookup.ResolveBandAsync(studentId, now, ct).ConfigureAwait(false);
        if (band is null)
        {
            return Results.Forbid();
        }

        if (!AgeBandPolicy.StudentHasAnyVetoRight(band.Value))
        {
            return Results.Forbid();
        }

        var instituteId = ResolveInstituteId(http);
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            return Results.Forbid();
        }

        var handler = new ConsentCommandHandler(piiAccessor);
        try
        {
            var evt = await handler.HandleAsync(new RestoreParentVisibility(
                StudentSubjectId: studentId,
                StudentBand: band.Value,
                Purpose: purpose,
                Initiator: VetoInitiator.Student,
                InitiatorActorId: studentId,
                InstituteId: instituteId,
                RestoredAt: now), ct).ConfigureAwait(false);

            await consentStore.AppendAsync(studentId, evt, ct).ConfigureAwait(false);
        }
        catch (ConsentAuthorizationException)
        {
            return Results.Forbid();
        }

        logger.LogInformation(
            "[prr-052] restore recorded: student={StudentId} band={Band} purpose={Purpose}",
            studentId, band.Value, purpose);

        return Results.Ok(new { Restored = true, Purpose = purpose.ToString() });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");

    private static string ResolveInstituteId(HttpContext http)
    {
        var claims = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        return claims.Count == 0 ? string.Empty : claims[0];
    }
}
