// =============================================================================
// Cena Platform — Household dashboard endpoint (EPIC-PRR-I PRR-324)
//
// Why this exists:
//   PRR-320 shipped GET /api/me/parent-dashboard (a flat list of students).
//   PRR-324 is the multi-student household view — primary + siblings +
//   household-wide rollups — for parents with 2+ linked students. It lives
//   at a distinct route (GET /api/me/household-dashboard) so the simpler
//   per-student surface can evolve independently and so the frontend can
//   split the two routes cleanly between the "dashboard" and "household"
//   Vue pages.
//
// Feature fence (persona #8 pricing-leak guard — PRR-343):
//   Only tiers with TierFeature.ParentDashboard = true may access. The
//   centralised SkuFeatureAuthorizer.CheckParent returns a stable
//   ReasonCode ("feature_not_in_sku" / "unsubscribed" / "unknown_feature")
//   that the Vue upsell modal localises. We do NOT re-implement the tier
//   lookup inline — the whole point of SkuFeatureAuthorizer is to prevent
//   drift between endpoints that check the same fence.
//
// IDOR guard:
//   Parent id comes from the JWT NameIdentifier claim (or "sub"
//   fallback). There is no path parameter or body field that lets the
//   caller specify a different parent. Same pattern as
//   ParentDashboardEndpoints.
//
// 404 vs 403:
//   - 404 when the parent has never activated a subscription (ActivatedAt
//     is null): the resource does not exist for this principal.
//   - 403 + stable ReasonCode when the parent IS activated but the
//     current tier doesn't include the feature (School SKU, or
//     deactivated retail subscription). The UI renders the "upgrade to
//     Premium" modal on 403 with a machine-readable code; it shows "no
//     household yet" on 404.
//
// Privacy line (ADR-0003):
//   The aggregator and DTO shapes are the privacy boundary. This endpoint
//   does NOT enrich cards with any non-summary data before returning —
//   it hands IHouseholdCardSource's output to the aggregator and returns.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Minimal-API endpoint for the multi-student household dashboard.</summary>
public static class HouseholdDashboardEndpoints
{
    /// <summary>Register <c>GET /api/me/household-dashboard</c>.</summary>
    public static IEndpointRouteBuilder MapHouseholdDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/household-dashboard", GetHouseholdDashboard)
            .WithTags("Subscriptions")
            .RequireAuthorization()
            .WithName("GetHouseholdDashboard");
        return app;
    }

    private static async Task<IResult> GetHouseholdDashboard(
        HttpContext http,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] IHouseholdCardSource cardSource,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        // ── IDOR guard: parent id comes from the authenticated principal. ───
        var parentId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? http.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return Results.Unauthorized();
        }

        // ── Load the parent's subscription aggregate. ───────────────────────
        var aggregate = await store.LoadAsync(parentId, ct);

        // 404 when the parent has never activated a subscription.
        if (aggregate.State.ActivatedAt is null)
        {
            return Results.NotFound();
        }

        // ── Feature fence: must be an Active tier with ParentDashboard. ─────
        // SkuFeatureAuthorizer.CheckParent already covers the "not Active"
        // and "feature not in SKU" cases with stable reason codes.
        var decision = SkuFeatureAuthorizer.CheckParent(
            aggregate.State, TierFeature.ParentDashboard);
        if (!decision.Allowed)
        {
            return Results.Json(
                new { error = "feature_denied", reasonCode = decision.ReasonCode },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // ── Defensive: an Active subscription must have at least one linked
        // student (the primary). If the list is empty, the subscription is
        // inconsistent — surface as 404 so the client doesn't render an
        // empty dashboard with misleading totals.
        var linked = aggregate.State.LinkedStudents;
        if (linked.Count == 0)
        {
            return Results.NotFound();
        }

        // ── Build cards via the configured source (Noop default, Marten
        // projection in follow-up), then fold through the pure aggregator
        // which enforces the invariants (primary at ordinal 0, siblings
        // sorted, totals = sum-across-all-cards).
        var now = clock.GetUtcNow();
        var cards = await cardSource.BuildCardsAsync(linked, now, ct);
        var response = HouseholdDashboardAggregator.Assemble(
            aggregate.State,
            cards,
            householdReadinessSummary: null); // No household-wide summary yet.

        return Results.Ok(response);
    }
}
