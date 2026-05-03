// =============================================================================
// Cena Platform — ResolvedPricing record (prr-244, ADR-0050 Q5)
//
// Output type of IInstitutePricingResolver.ResolveAsync. Carries the four
// concrete numbers + an operational provenance tag ("default" | "override")
// so finance dashboards + Stripe metadata can report whether a given
// invoice was priced off the global YAML default or a SUPER_ADMIN override.
// =============================================================================

namespace Cena.Actors.Pricing;

/// <summary>
/// Provenance of a resolved pricing record.
/// </summary>
public enum PricingSource
{
    /// <summary>Values came from <c>contracts/pricing/default-pricing.yml</c>.</summary>
    Default = 0,

    /// <summary>Values came from a SUPER_ADMIN-applied override document.</summary>
    Override = 1,
}

/// <summary>
/// A materialised pricing snapshot for one institute at one point in time.
/// Everything that bills, meters, or reports on pricing MUST consume this
/// (never a hard-coded literal — enforced by <c>NoHardcodedPricingTest</c>).
/// </summary>
/// <param name="StudentMonthlyPriceUsd">Per-student monthly subscription
/// (self-pay tier). Floor: $3.30 per the YAML bounds.</param>
/// <param name="InstitutionalPerSeatPriceUsd">Per-seat price when the
/// institute exceeds <see cref="MinSeatsForInstitutional"/>.</param>
/// <param name="MinSeatsForInstitutional">Seat-count breakpoint below
/// which <see cref="StudentMonthlyPriceUsd"/> applies.</param>
/// <param name="FreeTierSessionCap">Max free-tier sessions per student
/// per month before the gate flips to paid.</param>
/// <param name="Source">Default or Override.</param>
/// <param name="EffectiveFromUtc">When this pricing started applying. For
/// defaults this is the YAML <c>effective_from_utc</c>; for overrides this
/// is the override document's <c>EffectiveFromUtc</c>.</param>
public sealed record ResolvedPricing(
    decimal StudentMonthlyPriceUsd,
    decimal InstitutionalPerSeatPriceUsd,
    int MinSeatsForInstitutional,
    int FreeTierSessionCap,
    PricingSource Source,
    DateTimeOffset EffectiveFromUtc);
