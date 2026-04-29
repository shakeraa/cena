// =============================================================================
// Cena Platform — StudentEntitlementView (EPIC-PRR-I PRR-310, ADR-0057 §3)
//
// Per-student read model projected from subscription events. Consumed by:
//   - LLM router (ADR-0026 tiered routing) — caps per tier
//   - Diagnostic upload intake (EPIC-PRR-J) — per-tier caps
//   - Parent/teacher dashboard visibility gate
//   - Session pinning at session start (PRR-310)
//
// Projected key: studentSubjectId (wire-format encrypted per ADR-0038).
// Source: every event on `subscription-{parentId}` fans out to one
// update per currently-linked student.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Per-student entitlement snapshot. Immutable — the projection produces a
/// new instance on each subscription event fan-out.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">Student id (encrypted).</param>
/// <param name="EffectiveTier">Currently effective tier for this student.</param>
/// <param name="SourceParentSubjectIdEncrypted">Parent that entitled this student (encrypted).</param>
/// <param name="ValidUntil">When this entitlement expires (renewal boundary).</param>
/// <param name="LastUpdatedAt">Projection update timestamp.</param>
/// <param name="EffectiveStatus">
/// Current lifecycle status the entitlement is sourced from. UI distinguishes
/// "trialing" from "paid" by reading this; the LLM router reads this to keep
/// trial sessions on Haiku-default per design §4.2 (Trialing tier maps to
/// Haiku via <see cref="TierLlmRoutingPolicy"/> on the synthesised TrialPlus
/// tier). Defaults to <see cref="SubscriptionStatus.Active"/> for backward
/// compatibility with pre-trial call sites that pre-date this field.
/// </param>
/// <param name="TrialCaps">
/// Pinned trial caps (Phase 1D-fix item 1) — populated by the resolver when
/// <paramref name="EffectiveStatus"/> is <see cref="SubscriptionStatus.Trialing"/>.
/// Null on Active / PastDue / Unsubscribed / Expired views. The
/// <c>RequireEntitlementFilter</c> compares per-feature consumption against
/// these caps to gate cap-hit; carrying the caps on the view avoids a
/// parent-aggregate re-load on the hot path.
/// </param>
/// <param name="HasPaymentMethodOnFile">
/// True iff the parent stream has at least one
/// <see cref="Events.SubscriptionPaymentMethodAttached_V1"/> on file (Phase
/// 1D-fix item 2). Conversion-to-paid uses this to decide whether the SPA
/// can skip the card-collection step. The raw payment-method id is NOT
/// surfaced on the view (PCI-DSS scope minimisation).
/// </param>
public sealed record StudentEntitlementView(
    string StudentSubjectIdEncrypted,
    SubscriptionTier EffectiveTier,
    string SourceParentSubjectIdEncrypted,
    DateTimeOffset? ValidUntil,
    DateTimeOffset LastUpdatedAt,
    SubscriptionStatus EffectiveStatus = SubscriptionStatus.Active,
    Events.TrialCapsSnapshot? TrialCaps = null,
    bool HasPaymentMethodOnFile = false)
{
    /// <summary>Lookup the tier caps from the catalog for this entitlement.</summary>
    public UsageCaps Caps => TierCatalog.Get(EffectiveTier).Caps;

    /// <summary>Lookup the tier feature flags from the catalog.</summary>
    public TierFeatureFlags Features => TierCatalog.Get(EffectiveTier).Features;

    /// <summary>
    /// True if this entitlement grants the requested feature. Returns false
    /// for Unsubscribed or feature-flag-off.
    /// </summary>
    public bool HasFeature(TierFeature feature) => feature switch
    {
        TierFeature.ParentDashboard => Features.ParentDashboard,
        TierFeature.TutorHandoffPdf => Features.TutorHandoffPdf,
        TierFeature.ArabicDashboard => Features.ArabicDashboard,
        TierFeature.PrioritySupport => Features.PrioritySupport,
        TierFeature.ClassroomDashboard => Features.ClassroomDashboard,
        TierFeature.TeacherAssignedPractice => Features.TeacherAssignedPractice,
        TierFeature.Sso => Features.Sso,
        _ => false,
    };
}

/// <summary>Strongly-typed feature identifiers for entitlement checks.</summary>
public enum TierFeature
{
    ParentDashboard,
    TutorHandoffPdf,
    ArabicDashboard,
    PrioritySupport,
    ClassroomDashboard,
    TeacherAssignedPractice,
    Sso,
}
