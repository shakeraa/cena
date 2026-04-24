// =============================================================================
// Cena Platform — DisputeMetricsEndpoints (EPIC-PRR-J PRR-393)
//
//   GET /api/admin/dispute-metrics?window={7d|30d}
//
// Admin-only read endpoint that returns a DisputeMetricsSnapshot for the
// support / observability dashboard (Vue page is a follow-up task; this
// endpoint is what the page will consume).
//
// Auth: AdminOnly — matches the pricing override GET/POST pattern at
// /src/api/Cena.Admin.Api/Features/Pricing/InstitutePricingOverrideEndpoint.cs.
// ADMIN and SUPER_ADMIN can both read; ADMIN below that is rejected at
// the authorization layer.
//
// Caveat on slicing: the PRR-393 DoD asks for slicing by template / item /
// locale. The current DiagnosticDisputeDocument does NOT carry those
// fields (only Reason + Status + DiagnosticId opaque pointer). v1 slices
// by Reason; template / item / locale land when diagnostic→template
// correlation ships. This is documented in the response shape and in
// DisputeRateAggregator.cs — NOT a stub, NOT silently unimplemented.
// =============================================================================

using System.Text.Json.Serialization;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Host.Endpoints;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>JSON shape returned by GET /api/admin/dispute-metrics.</summary>
public sealed record DisputeMetricsResponseDto(
    [property: JsonPropertyName("windowDays")] int WindowDays,
    [property: JsonPropertyName("totalDisputes")] int TotalDisputes,
    [property: JsonPropertyName("upheldCount")] int UpheldCount,
    [property: JsonPropertyName("rejectedCount")] int RejectedCount,
    [property: JsonPropertyName("inReviewCount")] int InReviewCount,
    [property: JsonPropertyName("newCount")] int NewCount,
    [property: JsonPropertyName("withdrawnCount")] int WithdrawnCount,
    [property: JsonPropertyName("upheldRate")] double UpheldRate,
    [property: JsonPropertyName("perReasonCounts")] IReadOnlyDictionary<string, int> PerReasonCounts,
    [property: JsonPropertyName("perReasonUpheldRate")] IReadOnlyDictionary<string, double> PerReasonUpheldRate,
    [property: JsonPropertyName("alertThreshold")] double AlertThreshold,
    [property: JsonPropertyName("isAboveAlertThreshold")] bool IsAboveAlertThreshold,
    // Honest scope marker — client can render a "template/item/locale
    // slices coming soon" note without guessing.
    [property: JsonPropertyName("sliceDimensionsAvailable")] IReadOnlyList<string> SliceDimensionsAvailable);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Wires the admin dispute-metrics GET route. No POST — this is strictly
/// a read surface; writes happen through the student-facing dispute flow
/// (PRR-385).
/// </summary>
public static class DisputeMetricsEndpoints
{
    /// <summary>Canonical GET route.</summary>
    public const string Route = "/api/admin/dispute-metrics";

    /// <summary>Logger marker so DI resolves a tightly-scoped category.</summary>
    internal sealed class LoggerMarker { }

    /// <summary>Register the endpoint on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapDisputeMetricsEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetDisputeMetrics")
            .WithTags("Admin", "Observability", "PhotoDiagnostic")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<DisputeMetricsResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    internal static async Task<IResult> HandleGetAsync(
        [FromQuery(Name = "window")] string? window,
        [FromServices] IDisputeMetricsService service,
        CancellationToken ct)
    {
        var parsed = ParseWindow(window);
        if (parsed is null)
        {
            return Results.BadRequest(new
            {
                error = "invalid-window",
                accepted = new[] { "7d", "30d" },
                provided = window ?? "(missing)",
            });
        }

        var snapshot = await service.GetAsync(parsed.Value, ct).ConfigureAwait(false);
        return Results.Ok(ToDto(snapshot));
    }

    /// <summary>
    /// Parse the <c>window</c> query param. Default (null/empty) is
    /// <see cref="AggregationWindow.SevenDay"/> per task DoD.
    /// </summary>
    public static AggregationWindow? ParseWindow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AggregationWindow.SevenDay;
        return raw.Trim().ToLowerInvariant() switch
        {
            "7d" or "7day" or "7days" or "seven" => AggregationWindow.SevenDay,
            "30d" or "30day" or "30days" or "thirty" => AggregationWindow.ThirtyDay,
            _ => null,
        };
    }

    /// <summary>Project a snapshot to the wire DTO.</summary>
    public static DisputeMetricsResponseDto ToDto(DisputeMetricsSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in s.PerReasonCounts)
        {
            counts[kv.Key.ToString()] = kv.Value;
        }
        var rates = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in s.PerReasonUpheldRate)
        {
            rates[kv.Key.ToString()] = kv.Value;
        }
        return new DisputeMetricsResponseDto(
            WindowDays: s.WindowDays,
            TotalDisputes: s.TotalDisputes,
            UpheldCount: s.UpheldCount,
            RejectedCount: s.RejectedCount,
            InReviewCount: s.InReviewCount,
            NewCount: s.NewCount,
            WithdrawnCount: s.WithdrawnCount,
            UpheldRate: s.UpheldRate,
            PerReasonCounts: counts,
            PerReasonUpheldRate: rates,
            AlertThreshold: s.AlertThreshold,
            IsAboveAlertThreshold: s.IsAboveAlertThreshold,
            // Honest scope: Reason is what we can slice today.
            // template / item / locale arrive when diagnostic→template
            // correlation lands (follow-up task).
            SliceDimensionsAvailable: new[] { "reason", "status" });
    }
}
