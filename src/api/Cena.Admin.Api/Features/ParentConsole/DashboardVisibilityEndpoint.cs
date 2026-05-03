// =============================================================================
// Cena Platform — Parent Console: Dashboard-View Visibility Endpoint (prr-052)
//
// Surfaces the age-band-filtered dashboard field list a parent is allowed
// to see for a linked minor:
//
//   GET /api/v1/parent/minors/{studentAnonId}/dashboard-view
//
// Response body carries per-field visibility flags sourced from
// AgeBandPolicy.EvaluateDashboard(), which is the ONLY place in the
// codebase that branches on AgeBand. Parent-facing rendering code reads
// FieldKey + ParentCanSee + LegalBasisRef and decides its own layout.
//
// Authorisation:
//   - Caller role must be PARENT (admins use the admin console's own
//     per-feature endpoints; this route is not a support path).
//   - ParentAuthorizationGuard.AssertCanAccessAsync enforces binding +
//     tenant scoping (prr-009, ADR-0041). Any denial → 403 via
//     ForbiddenException → Results.Forbid().
//
// Age-band sourcing:
//   The SubjectBand comes from IStudentAgeBandLookup, which reads the
//   authoritative StudentProfileSnapshot.DateOfBirth. A request
//   parameter for age/band is NOT accepted — the endpoint ignores any
//   `band` query or body value a client tries to send.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Consent;
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
/// One row in the dashboard-view response. Mirrors
/// <see cref="ParentVisibleField"/> with string enums for wire stability.
/// </summary>
public sealed record DashboardVisibilityFieldDto(
    string FieldKey,
    string Kind,
    bool ParentCanSee,
    bool StudentSeesSameAsParent,
    bool StudentCanVeto,
    string LegalBasisRef);

/// <summary>
/// The GET /api/v1/parent/minors/{studentAnonId}/dashboard-view response.
/// </summary>
public sealed record DashboardVisibilityDto(
    string StudentAnonId,
    string SubjectBand,
    bool StudentCanSeeParentView,
    bool StudentHasAnyVetoRight,
    IReadOnlyList<DashboardVisibilityFieldDto> Fields);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Maps <c>GET /api/v1/parent/minors/{studentAnonId}/dashboard-view</c>.
/// </summary>
public static class DashboardVisibilityEndpoint
{
    /// <summary>Canonical route path.</summary>
    public const string Route = "/api/v1/parent/minors/{studentAnonId}/dashboard-view";

    /// <summary>Marker for ILogger&lt;T&gt; type-argument stability.</summary>
    internal sealed class DashboardVisibilityEndpointMarker { }

    /// <summary>Register the route on the builder.</summary>
    public static IEndpointRouteBuilder MapDashboardVisibilityEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetMinorDashboardVisibility")
            .WithTags("Parent Console", "Dashboard Visibility")
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> HandleGetAsync(
        string studentAnonId,
        HttpContext http,
        IStudentAgeBandLookup bandLookup,
        IConsentAggregateStore consentStore,
        ILogger<DashboardVisibilityEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        }

        // -- AuthZ: must be PARENT + pass the binding guard.
        if (!http.User.IsInRole("PARENT"))
        {
            return Results.Forbid();
        }

        ParentChildBindingResolution binding;
        try
        {
            binding = await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
        }
        catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
        {
            return Results.Forbid();
        }

        // -- Age-band: authoritative lookup from profile (never a request param).
        var now = DateTimeOffset.UtcNow;
        var band = await bandLookup.ResolveBandAsync(studentAnonId, now, ct).ConfigureAwait(false);
        if (band is null)
        {
            logger.LogWarning(
                "[prr-052] dashboard-view refused: no DOB on profile for student={StudentId}",
                studentAnonId);
            return Results.Forbid();
        }

        // -- Load current veto state from the consent aggregate.
        var aggregate = await consentStore.LoadAsync(studentAnonId, ct).ConfigureAwait(false);
        var vetoed = aggregate.State.VetoedParentVisibilityPurposes;

        // -- Evaluate policy.
        var policyOutput = AgeBandPolicy.EvaluateDashboard(new VisibilityPolicyInput(
            SubjectBand: band.Value,
            VetoedPurposes: vetoed,
            InstituteId: binding.InstituteId,
            InstitutePolicyAllowsVeto: true));

        var dto = new DashboardVisibilityDto(
            StudentAnonId: studentAnonId,
            SubjectBand: band.Value.ToString(),
            StudentCanSeeParentView: policyOutput.StudentCanSeeParentView,
            StudentHasAnyVetoRight: policyOutput.StudentHasAnyVetoRight,
            Fields: policyOutput.Fields
                .Select(f => new DashboardVisibilityFieldDto(
                    FieldKey: f.FieldKey,
                    Kind: f.Kind.ToString(),
                    ParentCanSee: f.ParentCanSee,
                    StudentSeesSameAsParent: f.StudentSeesSameAsParent,
                    StudentCanVeto: f.StudentCanVeto,
                    LegalBasisRef: f.LegalBasisRef))
                .ToList());

        logger.LogInformation(
            "[prr-052] dashboard-view resolved parent={ParentId} student={StudentId} "
                + "institute={InstituteId} band={Band} fields={FieldCount}",
            binding.ParentActorId,
            studentAnonId,
            binding.InstituteId,
            band.Value,
            policyOutput.Fields.Count);

        return Results.Ok(dto);
    }

    /// <summary>
    /// Invokes <see cref="ParentAuthorizationGuard.AssertCanAccessAsync"/>
    /// with DI-resolved services. Tracked by the
    /// <c>NoParentEndpointBypassesBindingTest</c> architecture ratchet.
    /// </summary>
    internal static async Task<ParentChildBindingResolution> RequireParentBindingAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var bindingService = services.GetRequiredService<IParentChildBindingService>();
        var logger = services.GetRequiredService<ILogger<DashboardVisibilityEndpointMarker>>();
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
