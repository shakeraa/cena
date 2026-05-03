// =============================================================================
// Cena Platform — SkuFeatureAuthorizer (EPIC-PRR-I PRR-343)
//
// Feature-fence helper. School-SKU accounts must NOT access the parent
// dashboard or tutor-handoff PDF — those are retail-tier differentiators
// whose non-overlap prevents parents and schools discovering each other's
// prices (persona #8 pricing-leak guard). The check itself is a one-line
// lookup into TierFeatureFlags via StudentEntitlementView.HasFeature, but
// centralising it here gives us:
//
//   1. A single reusable authorizer every endpoint calls. Drift between
//      endpoints becomes impossible; adding a new feature only flips a
//      flag in TierCatalog.
//   2. A testable seam — a TierCatalog edit that accidentally grants
//      ParentDashboard to SchoolSku would be caught by the assertion
//      suite attached to this file, not by a subtle in-endpoint inline
//      check that nobody tests.
//   3. A stable 403-reason code the UI can localize ("This feature is
//      not included in your institution plan — contact support to
//      upgrade") instead of a bare 403.
//
// The authorizer is a PURE static function; no DI, no I/O. Any endpoint
// can call Check(entitlement, feature) and branch on the bool + optional
// reason.
//
// Why not an ASP.NET attribute / AuthorizeAttribute? Because the
// per-request entitlement is resolved dynamically via
// IStudentEntitlementResolver, not present in the JWT claims. An
// attribute would force us to either bake the entitlement into the
// token (stale on tier changes) or inject the resolver into a custom
// policy evaluator (more machinery than a one-line call site). The
// inline-check pattern is cleaner at the cost of ~5 lines per endpoint.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Authorization decision returned by <see cref="SkuFeatureAuthorizer.Check"/>.</summary>
/// <param name="Allowed">True when the entitlement grants the feature.</param>
/// <param name="ReasonCode">
/// Stable machine-readable code when Allowed=false. One of:
/// <c>"feature_not_in_sku"</c> | <c>"unsubscribed"</c> |
/// <c>"unknown_feature"</c>. Null when Allowed=true.
/// </param>
public sealed record SkuFeatureDecision(bool Allowed, string? ReasonCode)
{
    /// <summary>Decision shorthand: allow.</summary>
    public static readonly SkuFeatureDecision Allow = new(true, null);

    /// <summary>Decision shorthand: deny, feature not in this SKU's catalog entry.</summary>
    public static readonly SkuFeatureDecision DenyNotInSku = new(false, "feature_not_in_sku");

    /// <summary>Decision shorthand: deny, no active subscription.</summary>
    public static readonly SkuFeatureDecision DenyUnsubscribed = new(false, "unsubscribed");

    /// <summary>Decision shorthand: deny, unrecognized feature code (defensive).</summary>
    public static readonly SkuFeatureDecision DenyUnknownFeature = new(false, "unknown_feature");
}

/// <summary>
/// Centralised feature-fence authorizer. Every endpoint that gates on
/// a tier feature should call <see cref="Check"/> and return 403 with
/// the reason code when <see cref="SkuFeatureDecision.Allowed"/> is false.
/// </summary>
public static class SkuFeatureAuthorizer
{
    /// <summary>
    /// Decide whether <paramref name="entitlement"/> may access the
    /// given <paramref name="feature"/>. Pure; no side effects.
    /// </summary>
    public static SkuFeatureDecision Check(
        StudentEntitlementView entitlement, TierFeature feature)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        if (entitlement.EffectiveTier == SubscriptionTier.Unsubscribed)
        {
            return SkuFeatureDecision.DenyUnsubscribed;
        }
        if (!Enum.IsDefined(feature))
        {
            return SkuFeatureDecision.DenyUnknownFeature;
        }
        return entitlement.HasFeature(feature)
            ? SkuFeatureDecision.Allow
            : SkuFeatureDecision.DenyNotInSku;
    }

    /// <summary>
    /// Convenience overload that also handles the most common endpoint-
    /// layer shape: given a parent's <see cref="SubscriptionState"/>,
    /// look up the tier's feature flags directly (no per-student
    /// entitlement resolver needed). Used by endpoints that operate on
    /// the parent subscription level (e.g., GET /api/me/parent-dashboard).
    /// </summary>
    public static SkuFeatureDecision CheckParent(
        SubscriptionState state, TierFeature feature)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != SubscriptionStatus.Active)
        {
            return SkuFeatureDecision.DenyUnsubscribed;
        }
        if (!Enum.IsDefined(feature))
        {
            return SkuFeatureDecision.DenyUnknownFeature;
        }

        var flags = TierCatalog.Get(state.CurrentTier).Features;
        var allowed = feature switch
        {
            TierFeature.ParentDashboard => flags.ParentDashboard,
            TierFeature.TutorHandoffPdf => flags.TutorHandoffPdf,
            TierFeature.ArabicDashboard => flags.ArabicDashboard,
            TierFeature.PrioritySupport => flags.PrioritySupport,
            TierFeature.ClassroomDashboard => flags.ClassroomDashboard,
            TierFeature.TeacherAssignedPractice => flags.TeacherAssignedPractice,
            TierFeature.Sso => flags.Sso,
            _ => false,
        };
        return allowed ? SkuFeatureDecision.Allow : SkuFeatureDecision.DenyNotInSku;
    }
}
