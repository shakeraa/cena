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
                if (await IsTrialCapReachedAsync(view, consumption, _feature, http.RequestAborted)
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
    /// Cap-hit decision. Compares per-student consumption against the pinned
    /// trial caps (carried on the view by the resolver since Phase 1D-fix).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decision matrix per cap value:
    ///   * cap == 0  → unbounded for this feature (TrialAllotmentConfig
    ///                 convention). Returns false — never gates.
    ///   * cap &gt; 0  → gates when consumption &gt;= cap. Returns true when
    ///                 the caller has already used up their allotment.
    /// </para>
    /// <para>
    /// For <see cref="EntitlementFeature.Generic"/> the gate is always false
    /// — the filter is then a pure entitlement (Active/Trialing/...) check
    /// without per-feature accounting. Returns false when
    /// <see cref="StudentEntitlementView.TrialCaps"/> is null (defensive —
    /// Trialing views always carry caps but a future state-graph extension
    /// might not).
    /// </para>
    /// </remarks>
    internal static async Task<bool> IsTrialCapReachedAsync(
        StudentEntitlementView view,
        IStudentTrialConsumptionStore consumption,
        EntitlementFeature feature,
        CancellationToken ct)
    {
        if (feature == EntitlementFeature.Generic) return false;
        if (view.TrialCaps is null) return false;

        var cap = feature switch
        {
            EntitlementFeature.TutorTurn => view.TrialCaps.TrialTutorTurns,
            EntitlementFeature.PhotoDiagnostic => view.TrialCaps.TrialPhotoDiagnostics,
            EntitlementFeature.PracticeSession => view.TrialCaps.TrialPracticeSessions,
            _ => 0,
        };
        // Cap of 0 means "no per-feature cap" — the platform admin chose
        // unbounded for this dimension via TrialAllotmentConfig.
        if (cap <= 0) return false;

        var snapshot = await consumption
            .GetAsync(view.StudentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);
        var used = feature switch
        {
            EntitlementFeature.TutorTurn => snapshot.TutorTurnsUsed,
            EntitlementFeature.PhotoDiagnostic => snapshot.PhotoDiagnosticsUsed,
            EntitlementFeature.PracticeSession => snapshot.SessionsStarted,
            _ => 0,
        };
        return used >= cap;
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
