// =============================================================================
// Cena Platform — UnitEconomicsRollup (EPIC-PRR-I PRR-330)
//
// Per-tier snapshot of subscription population + revenue. Computed by
// reading recent subscription events from Marten and aggregating. Consumed
// by the admin unit-economics dashboard.
//
// Values are in agorot (consistent with Money); formatting to display
// shekels is the UI's job.
//
// Integrity-first per memory "Honest not complimentary": CIs and honest
// numbers, not point estimates dressed as certainties. Confidence intervals
// are produced in a follow-up statistical layer; this rollup reports raw
// counts + amounts so downstream stages have accurate primitives.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Snapshot for a single tier in a rollup window.</summary>
/// <param name="Tier">Tier being described.</param>
/// <param name="ActiveSubscriptions">Count of subscriptions in Active status.</param>
/// <param name="PastDueSubscriptions">Count in PastDue.</param>
/// <param name="CancelledInWindow">Cancellations that happened inside the window.</param>
/// <param name="RefundedInWindow">Refunds issued inside the window.</param>
/// <param name="RevenueAgorot">Gross revenue realized (activations + renewals) inside the window.</param>
/// <param name="RefundsAgorot">Refund amounts issued inside the window.</param>
public sealed record TierSnapshot(
    SubscriptionTier Tier,
    int ActiveSubscriptions,
    int PastDueSubscriptions,
    int CancelledInWindow,
    int RefundedInWindow,
    long RevenueAgorot,
    long RefundsAgorot)
{
    /// <summary>Net revenue (gross − refunds) in agorot.</summary>
    public long NetRevenueAgorot => RevenueAgorot - RefundsAgorot;
}

/// <summary>Unit-economics snapshot across all tiers for a window.</summary>
/// <param name="WindowStart">Inclusive start of the window.</param>
/// <param name="WindowEnd">Exclusive end of the window.</param>
/// <param name="TierSnapshots">Per-tier snapshots.</param>
public sealed record UnitEconomicsSnapshot(
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<TierSnapshot> TierSnapshots)
{
    /// <summary>Total active subscriptions across all tiers.</summary>
    public int TotalActive => TierSnapshots.Sum(s => s.ActiveSubscriptions);

    /// <summary>Total gross revenue in agorot across all tiers in the window.</summary>
    public long TotalRevenueAgorot => TierSnapshots.Sum(s => s.RevenueAgorot);

    /// <summary>Total refunds in agorot across all tiers in the window.</summary>
    public long TotalRefundsAgorot => TierSnapshots.Sum(s => s.RefundsAgorot);

    /// <summary>Total net revenue (gross − refunds) across all tiers.</summary>
    public long TotalNetRevenueAgorot => TotalRevenueAgorot - TotalRefundsAgorot;
}
