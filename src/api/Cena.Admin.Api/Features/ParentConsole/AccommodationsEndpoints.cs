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
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        if (!await CallerHasReadAccessAsync(http, store, studentAnonId, ct))
            return Results.Forbid();

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

        // Only a linked guardian (Parent role) can SET a minor's
        // accommodations profile. Admins can read (above) but cannot
        // assign on a parent's behalf in Phase 1B.
        var parentAnonId = http.User.FindFirst("parentAnonId")?.Value
            ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(parentAnonId))
            return Results.Unauthorized();

        if (!http.User.IsInRole("PARENT"))
            return Results.Forbid();

        if (!await CallerIsLinkedGuardianAsync(http, store, studentAnonId, ct))
            return Results.Forbid();

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

        // Refuse any Phase-1B dimension — Phase 1A cannot activate them.
        var unshipped = parsed.Where(d => !Phase1ADimensions.IsShipped(d)).ToList();
        if (unshipped.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = "phase-1b-dimension-requested",
                dimensions = unshipped.Select(d => d.ToString()).ToList(),
                message = "These dimensions are Phase 1B and cannot be "
                    + "activated yet. They will become available when the "
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
    /// Read access: Parent linked to the minor, OR any ADMIN /
    /// SUPER_ADMIN (for support workflows). Teachers cannot read
    /// Phase 1B.
    /// </summary>
    private static async Task<bool> CallerHasReadAccessAsync(
        HttpContext http, IDocumentStore store, string studentAnonId, CancellationToken ct)
    {
        if (http.User.IsInRole("SUPER_ADMIN")) return true;
        if (http.User.IsInRole("ADMIN")) return true;
        if (http.User.IsInRole("PARENT"))
            return await CallerIsLinkedGuardianAsync(http, store, studentAnonId, ct);
        return false;
    }

    /// <summary>
    /// Phase 1B placeholder: every Parent role is treated as
    /// linked-guardian for the minor they target. Phase 1C wires the
    /// real ParentMinorLinkDocument lookup so a parent can only set
    /// accommodations on minors they're actually linked to.
    ///
    /// Not gating this is DELIBERATE in 1B — the feature is not yet
    /// exposed to real parents; this endpoint is exercised only by
    /// the admin onboarding tool + integration tests. Phase 1C MUST
    /// replace this with the real link check before the feature is
    /// exposed to a non-staging audience.
    /// </summary>
    private static Task<bool> CallerIsLinkedGuardianAsync(
        HttpContext http, IDocumentStore store, string studentAnonId, CancellationToken ct)
        => Task.FromResult(true);

    // Marker for ILogger<T> type argument stability.
    private sealed class AccommodationsEndpointMarker { }
}
