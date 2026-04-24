// =============================================================================
// Cena Platform — Admin unit-economics endpoint (EPIC-PRR-I PRR-330)
//
// GET /api/admin/unit-economics?windowDays=7|30
//
// Admin-role only. Returns the last N-day unit-economics snapshot keyed by
// tier. Honest numbers per memory "Honest not complimentary" — no fake
// confidence intervals; just the raw counts + sums that downstream stats
// can turn into CIs. CAC + LTV live separately (PRR-330 step 2).
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Admin unit-economics endpoint wiring.</summary>
public static class UnitEconomicsEndpoint
{
    /// <summary>Register <c>GET /api/admin/unit-economics</c>.</summary>
    public static IEndpointRouteBuilder MapUnitEconomicsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/unit-economics", GetAsync)
            .WithTags("Admin", "Subscriptions")
            .RequireAuthorization("AdminOnly")
            .WithName("GetUnitEconomics");
        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] UnitEconomicsAggregationService service,
        [FromServices] TimeProvider clock,
        [FromQuery] int windowDays,
        CancellationToken ct)
    {
        var days = windowDays <= 0 ? 7 : Math.Min(windowDays, 90);
        var now = clock.GetUtcNow();
        var snapshot = await service.ComputeAsync(
            now.AddDays(-days), now, ct);

        var rows = snapshot.TierSnapshots
            .Select(s => new TierEconomicsRowDto(
                TierId: s.Tier.ToString(),
                ActiveSubscriptions: s.ActiveSubscriptions,
                PastDueSubscriptions: s.PastDueSubscriptions,
                CancelledInWindow: s.CancelledInWindow,
                RefundedInWindow: s.RefundedInWindow,
                RevenueAgorot: s.RevenueAgorot,
                RefundsAgorot: s.RefundsAgorot,
                NetRevenueAgorot: s.NetRevenueAgorot))
            .ToArray();

        return Results.Ok(new UnitEconomicsResponseDto(
            WindowStart: snapshot.WindowStart,
            WindowEnd: snapshot.WindowEnd,
            Rows: rows,
            TotalActive: snapshot.TotalActive,
            TotalRevenueAgorot: snapshot.TotalRevenueAgorot,
            TotalRefundsAgorot: snapshot.TotalRefundsAgorot,
            TotalNetRevenueAgorot: snapshot.TotalNetRevenueAgorot));
    }
}
