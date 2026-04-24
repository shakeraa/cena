// =============================================================================
// Cena Platform — UnitEconomicsCalculator tests (EPIC-PRR-I PRR-330)
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class UnitEconomicsCalculatorTests
{
    [Fact]
    public void Window_with_three_premium_activations_counts_active_and_revenue()
    {
        var start = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var summaries = new[]
        {
            new SubscriptionSummary(SubscriptionTier.Premium, SubscriptionStatus.Active),
            new SubscriptionSummary(SubscriptionTier.Premium, SubscriptionStatus.Active),
            new SubscriptionSummary(SubscriptionTier.Premium, SubscriptionStatus.Active),
        };
        var events = new object[]
        {
            Activated(SubscriptionTier.Premium, 24_900, start.AddDays(1)),
            Activated(SubscriptionTier.Premium, 24_900, start.AddDays(2)),
            Activated(SubscriptionTier.Premium, 24_900, start.AddDays(3)),
        };
        var snap = UnitEconomicsCalculator.Compute(start, end, summaries, events);
        var premium = snap.TierSnapshots.Single(t => t.Tier == SubscriptionTier.Premium);
        Assert.Equal(3, premium.ActiveSubscriptions);
        Assert.Equal(74_700, premium.RevenueAgorot);
        Assert.Equal(74_700, snap.TotalRevenueAgorot);
    }

    [Fact]
    public void Window_includes_snapshots_for_all_retail_plus_school_sku()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-7);
        var end = DateTimeOffset.UtcNow;
        var snap = UnitEconomicsCalculator.Compute(
            start, end,
            Array.Empty<SubscriptionSummary>(),
            Array.Empty<object>());
        Assert.Contains(snap.TierSnapshots, s => s.Tier == SubscriptionTier.Basic);
        Assert.Contains(snap.TierSnapshots, s => s.Tier == SubscriptionTier.Plus);
        Assert.Contains(snap.TierSnapshots, s => s.Tier == SubscriptionTier.Premium);
        Assert.Contains(snap.TierSnapshots, s => s.Tier == SubscriptionTier.SchoolSku);
    }

    [Fact]
    public void Window_end_equal_or_before_start_throws()
    {
        var t = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() =>
            UnitEconomicsCalculator.Compute(
                t, t,
                Array.Empty<SubscriptionSummary>(),
                Array.Empty<object>()));
    }

    [Fact]
    public void Mixed_tier_summary_produces_separate_counts()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-7);
        var end = DateTimeOffset.UtcNow;
        var summaries = new[]
        {
            new SubscriptionSummary(SubscriptionTier.Basic, SubscriptionStatus.Active),
            new SubscriptionSummary(SubscriptionTier.Basic, SubscriptionStatus.PastDue),
            new SubscriptionSummary(SubscriptionTier.Premium, SubscriptionStatus.Active),
        };
        var snap = UnitEconomicsCalculator.Compute(start, end, summaries, Array.Empty<object>());
        var basic = snap.TierSnapshots.Single(t => t.Tier == SubscriptionTier.Basic);
        var premium = snap.TierSnapshots.Single(t => t.Tier == SubscriptionTier.Premium);
        Assert.Equal(1, basic.ActiveSubscriptions);
        Assert.Equal(1, basic.PastDueSubscriptions);
        Assert.Equal(1, premium.ActiveSubscriptions);
    }

    private static SubscriptionActivated_V1 Activated(
        SubscriptionTier tier, long grossAgorot, DateTimeOffset at) =>
        new(
            ParentSubjectIdEncrypted: $"enc::parent::{Guid.NewGuid():N}",
            PrimaryStudentSubjectIdEncrypted: $"enc::student::{Guid.NewGuid():N}",
            Tier: tier,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: grossAgorot,
            PaymentTransactionIdEncrypted: $"enc::txn::{Guid.NewGuid():N}",
            ActivatedAt: at,
            RenewsAt: at.AddMonths(1));
}
