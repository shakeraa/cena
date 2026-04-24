// =============================================================================
// Cena Platform — BillingCycle enum (EPIC-PRR-I, ADR-0057)
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Cadence at which the parent is charged. Annual = 10 months of monthly
/// price paid once (~17% off) per EPIC-PRR-I §3.1 PRR-292.
/// </summary>
public enum BillingCycle
{
    /// <summary>No active cycle (unsubscribed state).</summary>
    None = 0,

    /// <summary>Charged once per calendar month.</summary>
    Monthly = 1,

    /// <summary>Charged once per year. Commercial offer: 10-for-12 savings.</summary>
    Annual = 2,
}
