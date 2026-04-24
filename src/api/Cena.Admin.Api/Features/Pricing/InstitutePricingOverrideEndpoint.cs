// =============================================================================
// Cena Platform — Institute pricing override endpoint (prr-244, ADR-0050 Q5)
//
//   POST /api/admin/institutes/{id}/pricing-override
//   GET  /api/admin/institutes/{id}/pricing-override
//
// SUPER_ADMIN-only write path. ADMIN role (institute admins) is explicitly
// rejected with 403 on write — only platform SUPER_ADMIN can apply a per-
// institute pricing override. ADMIN may GET their own institute's pricing
// (read-only view for transparency).
//
// Write flow:
//   1. Validate input against bounds (delegates to DefaultPricingYaml).
//   2. Validate justification ≥20 chars (non-whitespace).
//   3. Compute old → new diff by loading the current resolved pricing.
//   4. Append InstitutePricingOverridden_V1 to the event stream.
//   5. Upsert the projection document.
//   6. Emit SIEM log tag `pricing.override.applied` with full field set.
//
// The endpoint is intentionally small (≤500 LOC per CLAUDE.md); heavier
// lifting lives in IInstitutePricingResolver + the store.
// =============================================================================

using System.Text.Json.Serialization;
using Cena.Actors.Pricing;
using Cena.Actors.Pricing.Events;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.Pricing;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>Override payload submitted by the SUPER_ADMIN admin UI.</summary>
public sealed record SetPricingOverrideRequest(
    [property: JsonPropertyName("studentMonthlyPriceUsd")]
    decimal StudentMonthlyPriceUsd,
    [property: JsonPropertyName("institutionalPerSeatPriceUsd")]
    decimal InstitutionalPerSeatPriceUsd,
    [property: JsonPropertyName("minSeatsForInstitutional")]
    int MinSeatsForInstitutional,
    [property: JsonPropertyName("freeTierSessionCap")]
    int FreeTierSessionCap,
    [property: JsonPropertyName("justificationText")]
    string JustificationText,
    [property: JsonPropertyName("effectiveFromUtc")]
    DateTimeOffset? EffectiveFromUtc,
    [property: JsonPropertyName("effectiveUntilUtc")]
    DateTimeOffset? EffectiveUntilUtc);

/// <summary>Response envelope for GET — current resolved pricing.</summary>
public sealed record PricingOverrideStatusDto(
    [property: JsonPropertyName("instituteId")]
    string InstituteId,
    [property: JsonPropertyName("studentMonthlyPriceUsd")]
    decimal StudentMonthlyPriceUsd,
    [property: JsonPropertyName("institutionalPerSeatPriceUsd")]
    decimal InstitutionalPerSeatPriceUsd,
    [property: JsonPropertyName("minSeatsForInstitutional")]
    int MinSeatsForInstitutional,
    [property: JsonPropertyName("freeTierSessionCap")]
    int FreeTierSessionCap,
    [property: JsonPropertyName("source")]
    string Source,
    [property: JsonPropertyName("effectiveFromUtc")]
    DateTimeOffset EffectiveFromUtc);

// ---- Event stream seam ------------------------------------------------------

/// <summary>
/// Writes events to the pricing-override append-only stream. Production
/// wiring is Marten; tests use an in-memory list for assertion.
/// </summary>
public interface IInstitutePricingEventPublisher
{
    /// <summary>Append the event to the stream keyed by institute id.</summary>
    Task AppendAsync(InstitutePricingOverridden_V1 evt, CancellationToken ct = default);
}

/// <summary>In-memory publisher for tests. Captures all appended events.</summary>
public sealed class InMemoryInstitutePricingEventPublisher : IInstitutePricingEventPublisher
{
    private readonly List<InstitutePricingOverridden_V1> _events = new();
    private readonly object _gate = new();

    /// <inheritdoc />
    public Task AppendAsync(InstitutePricingOverridden_V1 evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_gate) { _events.Add(evt); }
        return Task.CompletedTask;
    }

    /// <summary>Snapshot of all events appended so far.</summary>
    public IReadOnlyList<InstitutePricingOverridden_V1> Events
    {
        get { lock (_gate) { return _events.ToArray(); } }
    }
}

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Wires the GET + POST routes under <c>/api/admin/institutes/{id}/pricing-override</c>.
/// </summary>
public static class InstitutePricingOverrideEndpoint
{
    /// <summary>Canonical GET/POST route.</summary>
    public const string Route = "/api/admin/institutes/{instituteId}/pricing-override";

    /// <summary>Minimum justification length enforced on writes.</summary>
    public const int MinJustificationChars = 20;

    /// <summary>Log-tag used by SIEM ingest per PRR-244 audit requirements.</summary>
    public const string SiemLogTag = "pricing.override.applied";

    internal sealed class PricingOverrideMarker { }

    /// <summary>Map routes onto the supplied endpoint router.</summary>
    public static IEndpointRouteBuilder MapInstitutePricingOverrideEndpoint(
        this IEndpointRouteBuilder app)
    {
        // GET — ADMIN or SUPER_ADMIN. ADMIN sees only their own institute
        // (tenant scope enforced inside handler); SUPER_ADMIN sees all.
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetInstitutePricingOverride")
            .WithTags("Admin", "Pricing")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        // POST — SUPER_ADMIN ONLY. Institute admin getting a 403 here is
        // the expected response; the admin UI shows read-only "contact
        // Cena" copy to ADMIN callers.
        app.MapPost(Route, HandlePostAsync)
            .WithName("SetInstitutePricingOverride")
            .WithTags("Admin", "Pricing")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        return app;
    }

    internal static async Task<IResult> HandleGetAsync(
        [FromRoute] string instituteId,
        HttpContext http,
        [FromServices] IInstitutePricingResolver resolver,
        [FromServices] ILogger<PricingOverrideMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return Results.BadRequest(new { error = "missing-instituteId" });

        if (!IsTenantAllowed(http, instituteId))
        {
            logger.LogWarning(
                "[prr-244] pricing-override GET cross-tenant denied: requested={Institute}",
                instituteId);
            return Results.Forbid();
        }

        var resolved = await resolver.ResolveAsync(instituteId, ct).ConfigureAwait(false);
        return Results.Ok(new PricingOverrideStatusDto(
            InstituteId: instituteId,
            StudentMonthlyPriceUsd: resolved.StudentMonthlyPriceUsd,
            InstitutionalPerSeatPriceUsd: resolved.InstitutionalPerSeatPriceUsd,
            MinSeatsForInstitutional: resolved.MinSeatsForInstitutional,
            FreeTierSessionCap: resolved.FreeTierSessionCap,
            Source: resolved.Source == PricingSource.Override ? "override" : "default",
            EffectiveFromUtc: resolved.EffectiveFromUtc));
    }

    internal static async Task<IResult> HandlePostAsync(
        [FromRoute] string instituteId,
        [FromBody] SetPricingOverrideRequest request,
        HttpContext http,
        [FromServices] IInstitutePricingResolver resolver,
        [FromServices] IInstitutePricingOverrideStore overrideStore,
        [FromServices] IInstitutePricingEventPublisher eventPublisher,
        [FromServices] DefaultPricingYaml defaults,
        [FromServices] ILogger<PricingOverrideMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return Results.BadRequest(new { error = "missing-instituteId" });
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidateRequest(request, defaults.Bounds);
        if (validation is not null) return validation;

        // Snapshot current effective pricing for the diff fields on the event.
        var current = await resolver.ResolveAsync(instituteId, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var effectiveFrom = request.EffectiveFromUtc ?? now;
        var superAdminId = ResolveSuperAdminId(http);
        var traceId = http.TraceIdentifier;

        var evt = new InstitutePricingOverridden_V1(
            InstituteId: instituteId,
            OldStudentMonthlyPriceUsd: current.StudentMonthlyPriceUsd,
            NewStudentMonthlyPriceUsd: request.StudentMonthlyPriceUsd,
            OldInstitutionalPerSeatPriceUsd: current.InstitutionalPerSeatPriceUsd,
            NewInstitutionalPerSeatPriceUsd: request.InstitutionalPerSeatPriceUsd,
            OldMinSeatsForInstitutional: current.MinSeatsForInstitutional,
            NewMinSeatsForInstitutional: request.MinSeatsForInstitutional,
            OldFreeTierSessionCap: current.FreeTierSessionCap,
            NewFreeTierSessionCap: request.FreeTierSessionCap,
            JustificationText: request.JustificationText.Trim(),
            EffectiveFromUtc: effectiveFrom,
            EffectiveUntilUtc: request.EffectiveUntilUtc,
            OverriddenBySuperAdminId: superAdminId,
            TraceId: traceId,
            OccurredAtUtc: now);

        await eventPublisher.AppendAsync(evt, ct).ConfigureAwait(false);

        // Project into the document row so read-side + resolver have fresh state.
        var doc = new InstitutePricingOverrideDocument
        {
            Id = InstitutePricingOverrideDocument.IdFor(instituteId),
            InstituteId = instituteId,
            StudentMonthlyPriceUsd = request.StudentMonthlyPriceUsd,
            InstitutionalPerSeatPriceUsd = request.InstitutionalPerSeatPriceUsd,
            MinSeatsForInstitutional = request.MinSeatsForInstitutional,
            FreeTierSessionCap = request.FreeTierSessionCap,
            EffectiveFromUtc = effectiveFrom,
            EffectiveUntilUtc = request.EffectiveUntilUtc,
            OverriddenBySuperAdminId = superAdminId,
            JustificationText = request.JustificationText.Trim(),
            CreatedAtUtc = now,
        };
        await overrideStore.UpsertAsync(doc, ct).ConfigureAwait(false);

        // SIEM-tagged structured log — single line, one tag per PRR-244.
        // Note: justification_text is business-sensitive and may contain
        // strategic context; it's included in the SIEM feed (access-
        // controlled to SUPER_ADMIN + finance) but intentionally not in
        // lower-severity application logs.
        logger.LogInformation(
            "{SiemTag} super_admin_id={SuperAdmin} institute_id={Institute} "
            + "old_student_price={OldStudent} new_student_price={NewStudent} "
            + "old_per_seat_price={OldSeat} new_per_seat_price={NewSeat} "
            + "old_free_tier_cap={OldCap} new_free_tier_cap={NewCap} "
            + "effective_from={EffectiveFrom} trace_id={TraceId} "
            + "justification_text={Justification}",
            SiemLogTag, superAdminId, instituteId,
            evt.OldStudentMonthlyPriceUsd, evt.NewStudentMonthlyPriceUsd,
            evt.OldInstitutionalPerSeatPriceUsd, evt.NewInstitutionalPerSeatPriceUsd,
            evt.OldFreeTierSessionCap, evt.NewFreeTierSessionCap,
            effectiveFrom, traceId, evt.JustificationText);

        return Results.Ok(new PricingOverrideStatusDto(
            InstituteId: instituteId,
            StudentMonthlyPriceUsd: request.StudentMonthlyPriceUsd,
            InstitutionalPerSeatPriceUsd: request.InstitutionalPerSeatPriceUsd,
            MinSeatsForInstitutional: request.MinSeatsForInstitutional,
            FreeTierSessionCap: request.FreeTierSessionCap,
            Source: "override",
            EffectiveFromUtc: effectiveFrom));
    }

    /// <summary>
    /// Shared validation — exposed so tests can pin each rule without
    /// spinning up an ASP.NET pipeline.
    /// </summary>
    public static IResult? ValidateRequest(
        SetPricingOverrideRequest request,
        PricingBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bounds);

        if (string.IsNullOrWhiteSpace(request.JustificationText)
            || request.JustificationText.Trim().Length < MinJustificationChars)
        {
            return Results.BadRequest(new
            {
                error = "justification-too-short",
                minChars = MinJustificationChars,
            });
        }
        if (request.StudentMonthlyPriceUsd < bounds.StudentMonthlyPriceUsdMin
            || request.StudentMonthlyPriceUsd > bounds.StudentMonthlyPriceUsdMax)
        {
            return Results.BadRequest(new
            {
                error = "student-price-out-of-bounds",
                min = bounds.StudentMonthlyPriceUsdMin,
                max = bounds.StudentMonthlyPriceUsdMax,
                provided = request.StudentMonthlyPriceUsd,
            });
        }
        if (request.InstitutionalPerSeatPriceUsd < bounds.InstitutionalPerSeatPriceUsdMin
            || request.InstitutionalPerSeatPriceUsd > bounds.InstitutionalPerSeatPriceUsdMax)
        {
            return Results.BadRequest(new
            {
                error = "per-seat-price-out-of-bounds",
                min = bounds.InstitutionalPerSeatPriceUsdMin,
                max = bounds.InstitutionalPerSeatPriceUsdMax,
                provided = request.InstitutionalPerSeatPriceUsd,
            });
        }
        if (request.MinSeatsForInstitutional < bounds.MinSeatsForInstitutionalMin
            || request.MinSeatsForInstitutional > bounds.MinSeatsForInstitutionalMax)
        {
            return Results.BadRequest(new
            {
                error = "min-seats-out-of-bounds",
                min = bounds.MinSeatsForInstitutionalMin,
                max = bounds.MinSeatsForInstitutionalMax,
                provided = request.MinSeatsForInstitutional,
            });
        }
        if (request.FreeTierSessionCap < bounds.FreeTierSessionCapMin
            || request.FreeTierSessionCap > bounds.FreeTierSessionCapMax)
        {
            return Results.BadRequest(new
            {
                error = "free-tier-cap-out-of-bounds",
                min = bounds.FreeTierSessionCapMin,
                max = bounds.FreeTierSessionCapMax,
                provided = request.FreeTierSessionCap,
            });
        }
        if (request.InstitutionalPerSeatPriceUsd > request.StudentMonthlyPriceUsd)
        {
            return Results.BadRequest(new
            {
                error = "per-seat-must-not-exceed-student",
                studentMonthly = request.StudentMonthlyPriceUsd,
                perSeat = request.InstitutionalPerSeatPriceUsd,
            });
        }
        if (request.EffectiveUntilUtc is { } until
            && request.EffectiveFromUtc is { } from
            && until <= from)
        {
            return Results.BadRequest(new { error = "effective-until-must-follow-from" });
        }
        return null;
    }

    internal static bool IsTenantAllowed(HttpContext http, string instituteId)
    {
        if (http.User.IsInRole("SUPER_ADMIN")
            || http.User.HasClaim("role", "SUPER_ADMIN")
            || http.User.HasClaim(System.Security.Claims.ClaimTypes.Role, "SUPER_ADMIN"))
        {
            return true;
        }
        var claim = http.User.FindFirst("institute_id")?.Value;
        return !string.IsNullOrEmpty(claim)
            && string.Equals(claim, instituteId, StringComparison.Ordinal);
    }

    private static string ResolveSuperAdminId(HttpContext http)
    {
        return http.User.FindFirst("user_id")?.Value
            ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? http.User.Identity?.Name
            ?? "unknown-super-admin";
    }
}
