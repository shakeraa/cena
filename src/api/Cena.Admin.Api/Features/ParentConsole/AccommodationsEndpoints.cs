// =============================================================================
// Cena Platform — Parent Console: Accommodations Endpoints (RDY-066 Phase 1B)
//
// Parent-facing endpoints for reading + assigning a minor's
// accommodations profile. Every set / change emits an
// AccommodationProfileAssignedV1 event with a consent-document hash
// so the audit trail captures which parent granted what.
//
// Scoping:
//   PARENT       — must be linked to the minor as a registered guardian.
//   ADMIN        — SUPER_ADMIN or same-institute ADMIN can view (read-only)
//                  for support / onboarding. Cannot assign on behalf of a
//                  parent; that path requires Parent role.
//   TEACHER      — cannot read or set Phase 1A. Phase 2B adds teacher-on-
//                  behalf-of-student assignment with school-delegated
//                  consent (RDY-070 teacher console integration).
//
// Privacy (ADR-0003 + GDPR Art 9): accommodation data is GDPR Art. 9
// sensitive. Responses never include the parent's name, only the anon
// id; the audit event carries a ConsentDocumentHash, never the
// consent PDF itself.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Accommodations;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Security;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

public sealed record AccommodationProfileDto(
    string StudentAnonId,
    IReadOnlyList<string> EnabledDimensions,
    string? MinistryHatamaCode,
    string Assigner,
    DateTimeOffset AssignedAtUtc);

public sealed record SetAccommodationProfileRequest(
    IReadOnlyList<string> EnabledDimensions,
    string? MinistryHatamaCode,
    string? ConsentDocumentHash);

// ---- Endpoint ---------------------------------------------------------------

public static class AccommodationsEndpoints
{
    public const string GetRoute = "/api/v1/parent/minors/{studentAnonId}/accommodations";
    public const string SetRoute = "/api/v1/parent/minors/{studentAnonId}/accommodations";

    public static IEndpointRouteBuilder MapAccommodationsEndpoints(
        this IEndpointRouteBuilder app)
    {
        // GET — read the current profile for a minor
        app.MapGet(GetRoute, HandleGetAsync)
            .WithName("GetMinorAccommodations")
            .WithTags("Parent Console", "Accommodations")
            .RequireAuthorization();

        // PUT — assign / replace the profile
        app.MapPut(SetRoute, HandleSetAsync)
            .WithName("SetMinorAccommodations")
            .WithTags("Parent Console", "Accommodations")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleGetAsync(
        string studentAnonId,
        HttpContext http,
        IDocumentStore store,
        ILogger<AccommodationsEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });

        // prr-009 / ADR-0041: PARENT read MUST pass the binding guard.
        // ADMIN / SUPER_ADMIN retain the support-workflow read path
        // (tenant scope is enforced by Marten's TenantScope helper at
        // query time — not by this guard).
        var parentDenial = await AuthorizeReadOrForbidAsync(http, studentAnonId, ct).ConfigureAwait(false);
        if (parentDenial is not null) return parentDenial;

        using var session = store.QuerySession();
        // Fold the student's event stream for the latest
        // AccommodationProfileAssignedV1. Phase 1B reads the event
        // directly; Phase 1C wires a dedicated projection document
        // so the endpoint is O(1) instead of stream-scanning.
        var latest = await session.Events
            .QueryRawEventDataOnly<AccommodationProfileAssignedV1>()
            .Where(e => e.StudentAnonId == studentAnonId)
            .OrderByDescending(e => e.AssignedAtUtc)
            .Take(1)
            .ToListAsync(ct);

        if (latest.Count == 0)
        {
            // No profile configured → return the "empty" default so
            // the client doesn't have to special-case 404.
            return Results.Ok(new AccommodationProfileDto(
                StudentAnonId: studentAnonId,
                EnabledDimensions: Array.Empty<string>(),
                MinistryHatamaCode: null,
                Assigner: AccommodationAssigner.Self.ToString(),
                AssignedAtUtc: DateTimeOffset.MinValue));
        }

        var ev = latest[0];
        return Results.Ok(new AccommodationProfileDto(
            StudentAnonId: ev.StudentAnonId,
            EnabledDimensions: ev.EnabledDimensions
                .Select(d => d.ToString())
                .ToList(),
            MinistryHatamaCode: ev.MinistryHatamaCode,
            Assigner: ev.Assigner.ToString(),
            AssignedAtUtc: ev.AssignedAtUtc));
    }

    private static async Task<IResult> HandleSetAsync(
        string studentAnonId,
        SetAccommodationProfileRequest request,
        HttpContext http,
        IDocumentStore store,
        ILogger<AccommodationsEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        if (request is null)
            return Results.BadRequest(new { error = "missing-body" });

        // prr-009 / ADR-0041: SET is PARENT-only. The guard enforces
        // (parentActorId, studentAnonId, instituteId) binding via the
        // authoritative IParentChildBindingStore. Admins can READ (via
        // the GET handler above) but CANNOT assign on a parent's behalf
        // in Phase 1B — that path would require a signed-off teacher-
        // delegation consent event, scheduled for Phase 2B (RDY-070).
        if (!http.User.IsInRole("PARENT"))
            return Results.Forbid();

        ParentChildBindingResolution binding;
        try
        {
            binding = await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
        }
        catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
        {
            return Results.Forbid();
        }

        var parentAnonId = binding.ParentActorId;

        // Parse + validate requested dimensions. Unknown string tokens
        // are rejected rather than silently ignored; a Phase 1B client
        // that sends a typo should see a 400, not a partial write.
        var parsed = new List<AccommodationDimension>();
        foreach (var name in request.EnabledDimensions ?? Array.Empty<string>())
        {
            if (!Enum.TryParse<AccommodationDimension>(name, ignoreCase: true, out var dim))
                return Results.BadRequest(new
                {
                    error = "unknown-dimension",
                    dimension = name,
                });
            parsed.Add(dim);
        }

        // Refuse any unshipped dimension (neither Phase 1A nor the
        // additive Phase 1B set that prr-029 introduced with
        // LdAnxiousFriendly). Unshipped dimensions are silent-activation
        // risks: the UI cannot render them, so emitting a consent event
        // for one would produce the PRR-151 R-22 "consent without render"
        // defect class all over again.
        var unshipped = parsed
            .Where(d => !Phase1ADimensions.IsShipped(d) && !Phase1BDimensions.IsShipped(d))
            .ToList();
        if (unshipped.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = "unshipped-dimension-requested",
                dimensions = unshipped.Select(d => d.ToString()).ToList(),
                message = "These dimensions are not yet shipped and cannot "
                    + "be activated. They will become available when the "
                    + "corresponding UI layer ships.",
            });
        }

        // Emit the audit event. The parent-console consent flow is
        // expected to hash + persist the consent PDF separately; only
        // the hash travels in the event stream.
        var now = DateTimeOffset.UtcNow;
        var ev = new AccommodationProfileAssignedV1(
            StudentAnonId: studentAnonId,
            EnabledDimensions: parsed,
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: parentAnonId,
            MinistryHatamaCode: request.MinistryHatamaCode,
            ConsentDocumentHash: request.ConsentDocumentHash,
            AssignedAtUtc: now);

        await using var session = store.LightweightSession();
        session.Events.Append(studentAnonId, ev);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "[RDY-066] AccommodationProfile assigned for student={StudentAnonId} "
            + "by parent={ParentAnonId} dimensions=[{Dims}] ministry={Ministry}",
            studentAnonId,
            parentAnonId,
            string.Join(",", parsed),
            request.MinistryHatamaCode ?? "(none)");

        return Results.Ok(new AccommodationProfileDto(
            StudentAnonId: studentAnonId,
            EnabledDimensions: parsed.Select(d => d.ToString()).ToList(),
            MinistryHatamaCode: request.MinistryHatamaCode,
            Assigner: AccommodationAssigner.Parent.ToString(),
            AssignedAtUtc: now));
    }

    // ── Authorisation helpers ────────────────────────────────────────────

    /// <summary>
    /// prr-009: Read access.
    /// - PARENT: must pass the ParentAuthorizationGuard binding check.
    /// - ADMIN / SUPER_ADMIN: fall through; Marten's TenantScope filters
    ///   the query result to the caller's institute.
    /// - Anything else: 403.
    /// Returns null when authorized (let the handler proceed); returns
    /// a <see cref="Results.Forbid()"/> on denial.
    /// </summary>
    internal static async Task<IResult?> AuthorizeReadOrForbidAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        if (http.User.IsInRole("SUPER_ADMIN")) return null;
        if (http.User.IsInRole("ADMIN")) return null;

        if (http.User.IsInRole("PARENT"))
        {
            try
            {
                await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
                return null;
            }
            catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
            {
                return Results.Forbid();
            }
        }
        return Results.Forbid();
    }

    /// <summary>
    /// prr-009: resolve the institute id for the guard call. Source is
    /// the parent's <c>institute_id</c> claim issued at login; absence
    /// of the claim on a PARENT caller is a hard deny (no default).
    /// </summary>
    internal static string ResolveInstituteId(HttpContext http)
    {
        var claims = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        return claims.Count == 0 ? string.Empty : claims[0];
    }

    /// <summary>
    /// Invokes <see cref="ParentAuthorizationGuard.AssertCanAccessAsync"/>
    /// with DI-resolved services. Keeps the architecture ratchet's regex
    /// matching to exactly one well-known call site.
    /// </summary>
    internal static async Task<ParentChildBindingResolution> RequireParentBindingAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var bindingService = services.GetRequiredService<IParentChildBindingService>();
        var logger = services.GetRequiredService<ILogger<AccommodationsEndpointMarker>>();
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

    // Marker for ILogger<T> type argument stability.
    internal sealed class AccommodationsEndpointMarker { }
}
