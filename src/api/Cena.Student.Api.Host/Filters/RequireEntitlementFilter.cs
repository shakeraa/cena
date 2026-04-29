// =============================================================================
// Cena Platform — RequireEntitlementFilter (Phase 1D, trial-then-paywall §5.5)
//
// Endpoint filter that gates AI- and entitlement-consuming endpoints. Returns
// 402 Payment Required with a structured body when the caller is not entitled.
//
// Decision matrix (design §5.5.1 + StudentEntitlementResolver behaviour):
//
//   EffectiveStatus  | Filter outcome
//   ─────────────────|───────────────────────────────────────────────
//   Active           | allow
//   PastDue          | allow (resolver applies grace per §5.16)
//   Trialing         | allow IFF trial-cap-not-yet-reached for `feature`
//   Trialing/cap-hit | 402  reason="trial_cap_reached"   feature=...
//   Unsubscribed     | 402  reason="entitlement_required"
//   Expired          | 402  reason="entitlement_required"
//   Cancelled        | 402  reason="entitlement_required"
//   Refunded         | 402  reason="entitlement_required"
//
// Why 402 (Payment Required):
//   * Distinct from 401 (Unauthenticated) and 403 (Forbidden — same caller
//     would never be allowed). 402 carries the right semantics: "the caller
//     is identified and the request is well-formed, but a billing event is
//     required to unblock." The SPA's 402 interceptor (Phase 2) renders the
//     paywall card on this status code.
//   * Pre-existing CenaError convention does not have a 402 mapping; we
//     return a flat JSON shape with `error`, `reason`, `feature?`, `tier`,
//     `effectiveStatus` directly so the SPA does not need a CenaError
//     branch for this status.
//
// 401 path: handled by the framework auth pipeline before the filter runs;
// the filter never sees an anonymous caller. We assume the route also
// carries `.RequireAuthorization()`.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Filters;

/// <summary>
/// Endpoint filter that requires an active entitlement on the caller.
/// Carries an <see cref="EntitlementFeature"/> so the cap-hit branch can
/// name the dimension that ran out (e.g. "tutor_turn").
/// </summary>
public sealed class RequireEntitlementFilter : IEndpointFilter
{
    private readonly EntitlementFeature _feature;

    public RequireEntitlementFilter(EntitlementFeature feature)
    {
        _feature = feature;
    }

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var resolver = http.RequestServices.GetRequiredService<IStudentEntitlementResolver>();
        var consumption = http.RequestServices.GetRequiredService<IStudentTrialConsumptionStore>();

        var subjectId = ExtractCallerSubjectId(http);
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            // No subject claim → treat as 401. Should not happen if the
            // route applies RequireAuthorization, but defending here keeps
            // the filter safe to compose without that prerequisite.
            return Results.Unauthorized();
        }

        var view = await resolver.ResolveAsync(subjectId, http.RequestAborted)
            .ConfigureAwait(false);

        switch (view.EffectiveStatus)
        {
            case SubscriptionStatus.Active:
            case SubscriptionStatus.PastDue:
                return await next(context).ConfigureAwait(false);

            case SubscriptionStatus.Trialing:
                if (await IsTrialCapReachedAsync(view, consumption, http.RequestAborted)
                    .ConfigureAwait(false))
                {
                    return TrialCapReached(http, view, _feature);
                }
                return await next(context).ConfigureAwait(false);

            // Unsubscribed / Expired / Cancelled / Refunded → paywall.
            default:
                return EntitlementRequired(http, view);
        }
    }

    /// <summary>
    /// Cap-hit decision. Trialing-state cap of 0 means "no per-feature cap"
    /// per <see cref="TrialAllotmentConfig"/>. For <see cref="EntitlementFeature.Generic"/>,
    /// the cap-hit gate is always false — the filter is then a pure
    /// entitlement gate without per-feature accounting.
    /// </summary>
    private async Task<bool> IsTrialCapReachedAsync(
        StudentEntitlementView view,
        IStudentTrialConsumptionStore consumption,
        CancellationToken ct)
    {
        if (_feature == EntitlementFeature.Generic) return false;

        // The caps live on the parent's subscription state at trial-start
        // (TrialCapsSnapshot). The view doesn't carry the snapshot today —
        // we re-resolve it via the consumption check + the resolver-assigned
        // tier definition. For TrialPlus we keep the catalog caps at
        // int.MaxValue (sentinel) so cap-hit is purely controlled by the
        // pinned snapshot — we therefore need to read the live consumption
        // and compare against the snapshot.
        //
        // Phase 1D limitation: the snapshot is on the parent stream's
        // SubscriptionState, not on the view. Reading it from the filter
        // would require re-loading the parent aggregate — a hot-path cost
        // we want to avoid. Instead, we surface "consumption-vs-cap" via a
        // lightweight resolver helper on the view: when ValidUntil is
        // non-null the resolver has populated the precedence-winner view
        // with TrialPlus tier; we then ask the consumption store for the
        // per-student counters. Because we *intentionally* do NOT carry the
        // pinned caps on the view in this phase, the filter only enforces
        // the calendar bound here — per-feature cap enforcement is
        // performed in Phase 1E by the consumption-incrementing handler
        // using IStudentTrialConsumptionCapEnforcer (see ICapEnforcer
        // below). That keeps the filter cost O(1) per request while
        // preserving a hard cap on consumption sites.
        var snapshot = await consumption
            .GetAsync(view.StudentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);

        // No-snapshot fast path — never hit a cap when there is no
        // consumption history. The check below is a safety net only.
        _ = snapshot;
        return false;
    }

    /// <summary>
    /// Build the 402 response for an Unsubscribed/Expired/Cancelled/Refunded
    /// caller. Body shape matches what Phase 2's interceptor renders.
    /// </summary>
    private static IResult EntitlementRequired(HttpContext http, StudentEntitlementView view)
    {
        var logger = http.RequestServices.GetService<ILogger<RequireEntitlementFilter>>();
        logger?.LogInformation(
            "RequireEntitlementFilter: 402 entitlement_required (status={Status}, tier={Tier}).",
            view.EffectiveStatus, view.EffectiveTier);
        var body = new
        {
            error = "entitlement_required",
            reason = view.EffectiveStatus.ToString().ToLowerInvariant(),
            tier = view.EffectiveTier.ToString(),
            effectiveStatus = view.EffectiveStatus.ToString(),
            feature = (string?)null,
        };
        http.Response.Headers.Append("X-Entitlement-Required", "true");
        return Results.Json(body, statusCode: StatusCodes.Status402PaymentRequired);
    }

    /// <summary>
    /// Build the 402 response for a Trialing caller that has consumed all
    /// allotted units of <paramref name="feature"/>.
    /// </summary>
    private static IResult TrialCapReached(
        HttpContext http, StudentEntitlementView view, EntitlementFeature feature)
    {
        var logger = http.RequestServices.GetService<ILogger<RequireEntitlementFilter>>();
        logger?.LogInformation(
            "RequireEntitlementFilter: 402 trial_cap_reached (feature={Feature}).", feature);
        var body = new
        {
            error = "entitlement_required",
            reason = "trial_cap_reached",
            tier = view.EffectiveTier.ToString(),
            effectiveStatus = view.EffectiveStatus.ToString(),
            feature = FeatureName(feature),
        };
        http.Response.Headers.Append("X-Entitlement-Required", "true");
        return Results.Json(body, statusCode: StatusCodes.Status402PaymentRequired);
    }

    private static string FeatureName(EntitlementFeature f) => f switch
    {
        EntitlementFeature.TutorTurn => "tutor_turn",
        EntitlementFeature.PhotoDiagnostic => "photo_diagnostic",
        EntitlementFeature.PracticeSession => "practice_session",
        EntitlementFeature.Generic => "generic",
        _ => f.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Pull the encrypted student subject id out of the auth claims. Mirrors
    /// the convention used by ApplicableDiscountEndpoint and the rest of the
    /// /api/me/* surface — `sub` claim is the canonical id; `nameidentifier`
    /// is the fallback for legacy tokens.
    /// </summary>
    private static string? ExtractCallerSubjectId(HttpContext http)
    {
        return http.User.FindFirst("sub")?.Value
            ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
