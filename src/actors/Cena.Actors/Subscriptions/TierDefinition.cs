// =============================================================================
// Cena Platform — TierDefinition (EPIC-PRR-I PRR-291, ADR-0057 §6)
//
// Single source of truth for what a tier *is*: price in agorot, caps, and
// feature flags. Retrieved via TierCatalog.Get(tier). Not an interface —
// tier definitions are code constants per ADR-0057 §6, not operator-tunable.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Canonical tier definition. <see cref="TierCatalog"/> holds one instance
/// per <see cref="SubscriptionTier"/>.
/// </summary>
/// <param name="Tier">The tier this definition describes.</param>
/// <param name="MonthlyPrice">VAT-inclusive monthly price.</param>
/// <param name="AnnualPrice">
/// VAT-inclusive annual price. Per ADR-0057, retail tiers offer 10-for-12 at
/// launch (17% off); SchoolSku uses net-invoice terms (per-student monthly).
/// </param>
/// <param name="Caps">Usage caps for LLM / diagnostic / hint ladder.</param>
/// <param name="Features">Feature flags for endpoint authorization.</param>
/// <param name="IsRetail">
/// True for Basic/Plus/Premium; false for SchoolSku (B2B with net-invoice
/// billing, different payment flow).
/// </param>
/// <param name="IsDecoy">
/// Plus is marked decoy per ADR-0057 §5-review. Used ONLY for internal
/// analytics + A/B harness (PRR-332); never exposed to the UI (would defeat
/// the mechanic and persona #3 trust signal).
/// </param>
public sealed record TierDefinition(
    SubscriptionTier Tier,
    Money MonthlyPrice,
    Money AnnualPrice,
    UsageCaps Caps,
    TierFeatureFlags Features,
    bool IsRetail,
    bool IsDecoy);
