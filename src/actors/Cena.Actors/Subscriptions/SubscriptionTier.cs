// =============================================================================
// Cena Platform — SubscriptionTier enum (EPIC-PRR-I, ADR-0057)
//
// Enumerates the five subscription tiers. Four retail + one B2B school SKU
// sharing the same aggregate (feature-fenced at the endpoint layer via
// SkuFeatureAuthorizer per ADR-0057 §8). Never reorder or remove members
// without a migration — values are persisted on events.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Cena subscription tiers. Default = <see cref="Unsubscribed"/> before any
/// commercial activation. Do not renumber; values persist in events.
/// </summary>
public enum SubscriptionTier
{
    /// <summary>No active subscription. Read-only or free-locked UX.</summary>
    Unsubscribed = 0,

    /// <summary>
    /// Retail Basic — ₪79/mo. Haiku-first routing with capped Sonnet
    /// escalations. No photo diagnostic. 1 student seat.
    /// </summary>
    Basic = 1,

    /// <summary>
    /// Retail Plus — ₪229/mo (decoy per review 2026-04-22). Sonnet-for-complex,
    /// unlimited photo diagnostic, no parent dashboard, no tutor-handoff PDF.
    /// </summary>
    Plus = 2,

    /// <summary>
    /// Retail Premium — ₪249/mo (target). Sonnet unlimited soft-capped, photo
    /// diagnostic 100/mo soft + 300/mo hard, parent/teacher dashboard, Arabic
    /// parity, tutor-handoff PDF, priority support.
    /// </summary>
    Premium = 3,

    /// <summary>
    /// B2B school SKU — ~₪35/student/mo. Classroom admin dashboard, teacher
    /// assignment, SSO. Parent-dashboard + tutor-handoff feature-fenced OUT
    /// per ADR-0057 §8.
    /// </summary>
    SchoolSku = 4,

    /// <summary>
    /// Trial entitlement (synthetic). Carried on <see cref="StudentEntitlementView.EffectiveTier"/>
    /// while the parent stream is in <see cref="SubscriptionStatus.Trialing"/>.
    /// Caps are resolved from the per-trial <see cref="Events.TrialCapsSnapshot"/>
    /// pinned at trial-start, NOT from this tier's catalog entry — the
    /// <see cref="TierCatalog"/> entry exists so the catalog lookup is total
    /// (every value of this enum has a definition). The catalog entry's caps
    /// are deliberately set to <see cref="int.MaxValue"/> sentinels so a
    /// caller that mis-uses the catalog directly (instead of the resolver-
    /// synthesised view) cannot accidentally over-restrict a trial; the
    /// resolver fans the live snapshot caps onto the view.
    /// </summary>
    TrialPlus = 5,
}
