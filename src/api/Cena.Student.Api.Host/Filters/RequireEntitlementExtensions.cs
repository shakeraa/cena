// =============================================================================
// Cena Platform — RequireEntitlementExtensions (Phase 1D filter glue)
//
// Fluent extension that attaches a RequireEntitlementFilter to a route
// handler. Consumers write:
//
//   app.MapPost("/api/sessions/{sid}/tutor-turn", Handler)
//      .RequireAuthorization()
//      .RequireActiveEntitlement(EntitlementFeature.TutorTurn);
//
// Phase 1D ships the filter and this extension; Phase 1E applies the
// extension to the actual tutor / photo / session endpoints.
// =============================================================================

using Cena.Actors.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cena.Api.Host.Filters;

/// <summary>Extension methods that attach <see cref="RequireEntitlementFilter"/>.</summary>
public static class RequireEntitlementExtensions
{
    /// <summary>
    /// Attach a <see cref="RequireEntitlementFilter"/> for the given feature.
    /// Routes that opt-in via this extension return 402 to non-entitled
    /// callers; allow Active / PastDue / Trialing-without-cap-hit through.
    /// </summary>
    public static RouteHandlerBuilder RequireActiveEntitlement(
        this RouteHandlerBuilder builder, EntitlementFeature feature)
    {
        return builder.AddEndpointFilter(new RequireEntitlementFilter(feature));
    }

    /// <inheritdoc cref="RequireActiveEntitlement(RouteHandlerBuilder, EntitlementFeature)" />
    public static RouteGroupBuilder RequireActiveEntitlement(
        this RouteGroupBuilder builder, EntitlementFeature feature)
    {
        return builder.AddEndpointFilter(new RequireEntitlementFilter(feature));
    }
}
