// =============================================================================
// Cena Platform — UnitEconomicsAdminEndpoints (EPIC-PRR-I PRR-330)
//
//   GET /api/admin/unit-economics/history?weeks=12
//
// Admin-only read endpoint that returns the last N weekly rollup snapshots
// produced by UnitEconomicsRollupWorker. The admin Vue dashboard's history
// chart consumes this shape (the Vue page is a separate follow-up task —
// this endpoint is the contract it will read against).
//
// Auth: AdminOnly — matches InstitutePricingOverrideEndpoint + DisputeMetrics
// endpoints. ADMIN + SUPER_ADMIN can read; anything below that is rejected
// at the authorization layer. No per-institute scoping because unit
// economics is a platform-level metric (ADR-0057 §8 — tier revenue is not
// tenant-private); if we later need per-institute slicing it will move to
// a separate SameOrg-guarded endpoint.
//
// The current-week view is served by the existing sibling endpoint
// GET /api/admin/unit-economics (UnitEconomicsEndpoint.cs on the Student
// API host). This history endpoint complements that single-window view
// by exposing the trend line without re-computing per request.
// =============================================================================

using System.Text.Json.Serialization;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Host.Endpoints;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>Per-week entry on the history response.</summary>
public sealed record UnitEconomicsHistoryRowDto(
    [property: JsonPropertyName("weekId")] string WeekId,
    [property: JsonPropertyName("weekStartUtc")] DateTimeOffset WeekStartUtc,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("totals")] UnitEconomicsResponseDto Totals);

/// <summary>JSON shape returned by GET /api/admin/unit-economics/history.</summary>
public sealed record UnitEconomicsHistoryResponseDto(
    [property: JsonPropertyName("weeksRequested")] int WeeksRequested,
    [property: JsonPropertyName("weeksReturned")] int WeeksReturned,
    [property: JsonPropertyName("weeks")] IReadOnlyList<UnitEconomicsHistoryRowDto> Weeks);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Wires the admin unit-economics history GET route. No POST here — the
/// snapshots are written by <see cref="UnitEconomicsRollupWorker"/>, never
/// by the UI.
/// </summary>
public static class UnitEconomicsAdminEndpoints
{
    /// <summary>Canonical GET route.</summary>
    public const string Route = "/api/admin/unit-economics/history";

    /// <summary>Default + cap on the <c>weeks</c> query param.</summary>
    public const int DefaultWeeks = 12;

    /// <summary>
    /// Absolute upper bound — 52 weeks = one year. Keeps the memory + bandwidth
    /// footprint of a single request bounded and matches the admin chart's
    /// widest useful time-axis.
    /// </summary>
    public const int MaxWeeks = 52;

    /// <summary>Register the endpoint on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapUnitEconomicsAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetUnitEconomicsHistory")
            .WithTags("Admin", "Subscriptions", "Observability")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<UnitEconomicsHistoryResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Handle a history GET. Public so tests in sibling assemblies can drive
    /// it without a TestServer. Matches the public-helper pattern used by
    /// InstitutePricingOverrideEndpoint.ValidateRequest.
    /// </summary>
    public static async Task<IResult> HandleGetAsync(
        [FromServices] IUnitEconomicsSnapshotStore store,
        [FromQuery(Name = "weeks")] int? weeks,
        CancellationToken ct)
    {
        var requested = ClampWeeks(weeks);
        var rows = await store.ListRecentAsync(requested, ct).ConfigureAwait(false);

        var wire = rows.Select(ToWireRow).ToArray();
        return Results.Ok(new UnitEconomicsHistoryResponseDto(
            WeeksRequested: requested,
            WeeksReturned: wire.Length,
            Weeks: wire));
    }

    /// <summary>
    /// Clamp the <c>weeks</c> query param to <c>[1, MaxWeeks]</c>. Null or
    /// non-positive → default. Exposed public so tests can pin the
    /// boundary without spinning up an ASP.NET pipeline.
    /// </summary>
    public static int ClampWeeks(int? weeks)
    {
        if (weeks is null || weeks <= 0) return DefaultWeeks;
        if (weeks > MaxWeeks) return MaxWeeks;
        return weeks.Value;
    }

    /// <summary>
    /// Project a persisted document row to the wire shape. Re-uses the
    /// existing <see cref="UnitEconomicsResponseDto"/> so the admin UI's
    /// single-window view and its history chart see the same per-tier
    /// DTO — one JSON parser, one set of field names.
    /// </summary>
    public static UnitEconomicsHistoryRowDto ToWireRow(UnitEconomicsSnapshotDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var s = doc.Snapshot;
        var rows = s.TierSnapshots
            .Select(t => new TierEconomicsRowDto(
                TierId: t.Tier.ToString(),
                ActiveSubscriptions: t.ActiveSubscriptions,
                PastDueSubscriptions: t.PastDueSubscriptions,
                CancelledInWindow: t.CancelledInWindow,
                RefundedInWindow: t.RefundedInWindow,
                RevenueAgorot: t.RevenueAgorot,
                RefundsAgorot: t.RefundsAgorot,
                NetRevenueAgorot: t.NetRevenueAgorot))
            .ToArray();

        return new UnitEconomicsHistoryRowDto(
            WeekId: doc.Id,
            WeekStartUtc: doc.WeekStartUtc,
            GeneratedAtUtc: doc.GeneratedAtUtc,
            Totals: new UnitEconomicsResponseDto(
                WindowStart: s.WindowStart,
                WindowEnd: s.WindowEnd,
                Rows: rows,
                TotalActive: s.TotalActive,
                TotalRevenueAgorot: s.TotalRevenueAgorot,
                TotalRefundsAgorot: s.TotalRefundsAgorot,
                TotalNetRevenueAgorot: s.TotalNetRevenueAgorot));
    }
}
