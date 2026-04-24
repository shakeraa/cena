// =============================================================================
// Cena Platform — Parent Console: Digest Preferences Endpoints (prr-051).
//
// Parent-facing endpoints for reading + updating per-child digest preferences.
// Mirrors AccommodationsEndpoints: every call routes through
// ParentAuthorizationGuard, tenant scoping is preserved, and the POST handler
// emits a ParentDigestPreferencesUpdated_V1 event for audit.
//
// Endpoints:
//   GET  /api/v1/parent/digest/preferences?studentAnonId=<id>
//   POST /api/v1/parent/digest/preferences
//     body: { studentAnonId, purposes: { weekly_summary: "opted_in", ... } }
//
// GET semantics:
//   - If no row exists, returns the default-resolved shape (safety_alerts
//     opted_in; every other purpose opted_out). The caller NEVER sees
//     a NotSet in the response; the default table makes the answer concrete.
//
// POST semantics:
//   - Accepts a partial map of purposes → { opted_in | opted_out }.
//     Purposes not mentioned are left unchanged.
//   - Unknown purposes → 400 with `unknown-purpose`. A typo must not silently
//     overwrite a different preference (same defensive posture as
//     AccommodationsEndpoints.HandleSetAsync).
//   - Emits ParentDigestPreferencesUpdated_V1 and returns the post-write state.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.ParentDigest;
using Cena.Actors.ParentDigest.Events;
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

public sealed record ParentDigestPreferenceDto(
    string Purpose,
    string Status);

public sealed record ParentDigestPreferencesDto(
    string StudentAnonId,
    IReadOnlyList<ParentDigestPreferenceDto> Purposes,
    bool Unsubscribed,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? UnsubscribedAtUtc);

public sealed record SetParentDigestPreferencesRequest(
    string StudentAnonId,
    IReadOnlyDictionary<string, string>? Purposes);

// ---- Endpoint ---------------------------------------------------------------

public static class ParentDigestPreferencesEndpoints
{
    public const string GetRoute = "/api/v1/parent/digest/preferences";
    public const string SetRoute = "/api/v1/parent/digest/preferences";

    // Status wire-format tokens. A single source of truth so the ToWire /
    // TryParseWire helpers stay in lockstep with the API contract.
    internal const string StatusOptedInWire = "opted_in";
    internal const string StatusOptedOutWire = "opted_out";

    public static IEndpointRouteBuilder MapParentDigestPreferencesEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(GetRoute, HandleGetAsync)
            .WithName("GetParentDigestPreferences")
            .WithTags("Parent Console", "Digest")
            .RequireAuthorization();

        app.MapPost(SetRoute, HandleSetAsync)
            .WithName("SetParentDigestPreferences")
            .WithTags("Parent Console", "Digest")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleGetAsync(
        string? studentAnonId,
        HttpContext http,
        IParentDigestPreferencesStore store,
        ILogger<ParentDigestPreferencesEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });

        // prr-009 / ADR-0041: PARENT-only. Admins do NOT read preferences on
        // behalf of parents — preferences are a parent intent surface and
        // leaking the opt-in state to an admin without parent consent is a
        // GDPR leak. A support-workflow read path can be added later via
        // a sibling endpoint with its own audit event.
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

        var nowUtc = DateTimeOffset.UtcNow;
        var preferences = await store.FindAsync(
            binding.ParentActorId,
            binding.StudentSubjectId,
            binding.InstituteId,
            ct).ConfigureAwait(false);

        preferences ??= ParentDigestPreferences.Empty(
            binding.ParentActorId,
            binding.StudentSubjectId,
            binding.InstituteId,
            nowUtc);

        return Results.Ok(ToDto(studentAnonId, preferences));
    }

    private static async Task<IResult> HandleSetAsync(
        SetParentDigestPreferencesRequest request,
        HttpContext http,
        IParentDigestPreferencesStore store,
        IDocumentStore documentStore,
        ILogger<ParentDigestPreferencesEndpointMarker> logger,
        CancellationToken ct)
    {
        if (request is null)
            return Results.BadRequest(new { error = "missing-body" });
        if (string.IsNullOrWhiteSpace(request.StudentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        if (request.Purposes is null || request.Purposes.Count == 0)
            return Results.BadRequest(new { error = "missing-purposes" });

        if (!http.User.IsInRole("PARENT"))
            return Results.Forbid();

        ParentChildBindingResolution binding;
        try
        {
            binding = await RequireParentBindingAsync(http, request.StudentAnonId, ct)
                .ConfigureAwait(false);
        }
        catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
        {
            return Results.Forbid();
        }

        // Parse + validate every wire token BEFORE touching the store — a
        // partial write on a typo'd client is a worse outcome than a 400.
        var updates = ImmutableDictionary.CreateBuilder<DigestPurpose, OptInStatus>();
        foreach (var (purposeWire, statusWire) in request.Purposes)
        {
            if (!DigestPurposes.TryParseWire(purposeWire, out var purpose))
            {
                return Results.BadRequest(new
                {
                    error = "unknown-purpose",
                    purpose = purposeWire,
                });
            }
            if (!TryParseStatusWire(statusWire, out var status))
            {
                return Results.BadRequest(new
                {
                    error = "unknown-status",
                    purpose = purposeWire,
                    status = statusWire,
                });
            }
            updates[purpose] = status;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var updated = await store.ApplyUpdateAsync(
            binding.ParentActorId,
            binding.StudentSubjectId,
            binding.InstituteId,
            updates.ToImmutable(),
            nowUtc,
            ct).ConfigureAwait(false);

        // Append the audit event to the parent-child stream so compliance
        // auditors have a non-repudiable trail. Stream key mirrors the
        // AccommodationsEndpoints precedent (student-stream-keyed so the
        // GDPR export endpoint folds everything about a minor into one
        // query).
        var ev = new ParentDigestPreferencesUpdated_V1(
            ParentActorId: binding.ParentActorId,
            StudentSubjectId: binding.StudentSubjectId,
            InstituteId: binding.InstituteId,
            PurposeStatuses: updated.PurposeStatuses,
            UpdatedAtUtc: nowUtc);

        await using var session = documentStore.LightweightSession();
        session.Events.Append(binding.StudentSubjectId, ev);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "[prr-051] ParentDigestPreferences updated for student={StudentAnonId} "
            + "parent={ParentAnonId} institute={InstituteId} purposes=[{Purposes}]",
            binding.StudentSubjectId,
            binding.ParentActorId,
            binding.InstituteId,
            string.Join(",", updates.Select(kv =>
                $"{DigestPurposes.ToWire(kv.Key)}={ToStatusWire(kv.Value)}")));

        return Results.Ok(ToDto(request.StudentAnonId, updated));
    }

    // ── Mapping helpers ─────────────────────────────────────────────────

    internal static ParentDigestPreferencesDto ToDto(
        string studentAnonId, ParentDigestPreferences preferences)
    {
        var rows = new List<ParentDigestPreferenceDto>(DigestPurposes.KnownPurposes.Length);
        foreach (var purpose in DigestPurposes.KnownPurposes)
        {
            // Use EffectiveStatus so the UI never has to know about the
            // default table — the server pre-applies it.
            rows.Add(new ParentDigestPreferenceDto(
                Purpose: DigestPurposes.ToWire(purpose),
                Status: ToStatusWire(preferences.EffectiveStatus(purpose))));
        }
        return new ParentDigestPreferencesDto(
            StudentAnonId: studentAnonId,
            Purposes: rows,
            Unsubscribed: preferences.UnsubscribedAtUtc is not null,
            UpdatedAtUtc: preferences.UpdatedAtUtc,
            UnsubscribedAtUtc: preferences.UnsubscribedAtUtc);
    }

    internal static string ToStatusWire(OptInStatus status) => status switch
    {
        OptInStatus.OptedIn => StatusOptedInWire,
        OptInStatus.OptedOut => StatusOptedOutWire,
        // NotSet should never leave the server — the default table resolves
        // it before DTO build. If it somehow reaches here, render as
        // "opted_out" (the conservative default) rather than leaking an
        // internal token to the client.
        _ => StatusOptedOutWire,
    };

    internal static bool TryParseStatusWire(string wire, out OptInStatus status)
    {
        status = OptInStatus.NotSet;
        if (string.IsNullOrWhiteSpace(wire)) return false;
        switch (wire.Trim().ToLowerInvariant())
        {
            case StatusOptedInWire: status = OptInStatus.OptedIn; return true;
            case StatusOptedOutWire: status = OptInStatus.OptedOut; return true;
            default: return false;
        }
    }

    // ── Authorisation helpers (mirrors AccommodationsEndpoints) ─────────

    internal static string ResolveInstituteId(HttpContext http)
    {
        var claims = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        return claims.Count == 0 ? string.Empty : claims[0];
    }

    internal static async Task<ParentChildBindingResolution> RequireParentBindingAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var bindingService = services.GetRequiredService<IParentChildBindingService>();
        var logger = services.GetRequiredService<ILogger<ParentDigestPreferencesEndpointMarker>>();
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

    internal sealed class ParentDigestPreferencesEndpointMarker { }
}
