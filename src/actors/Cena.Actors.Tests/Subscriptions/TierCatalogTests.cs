// =============================================================================
// Cena Platform — TierCatalog integrity tests (EPIC-PRR-I PRR-291)
//
// Locks in the launch prices. If these tests fail, a PR has changed pricing
// without explicit review by the pricing decision-holder — which is exactly
// the signal we want (memory "Labels match data" + "No trade-offs").
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TierCatalogTests
{
    [Fact]
    public void Basic_monthly_price_is_7900_agorot()
    {
        var tier = TierCatalog.Get(SubscriptionTier.Basic);
        Assert.Equal(7_900L, tier.MonthlyPrice.Amount);
    }

    [Fact]
    public void Plus_monthly_price_is_22900_agorot_and_marked_as_decoy()
    {
        var tier = TierCatalog.Get(SubscriptionTier.Plus);
        Assert.Equal(22_900L, tier.MonthlyPrice.Amount);
        Assert.True(tier.IsDecoy, "Plus must be flagged as decoy for internal analytics.");
    }

    [Fact]
    public void Premium_monthly_price_is_24900_agorot_and_not_decoy()
    {
        var tier = TierCatalog.Get(SubscriptionTier.Premium);
        Assert.Equal(24_900L, tier.MonthlyPrice.Amount);
        Assert.False(tier.IsDecoy);
    }

    [Fact]
    public void Annual_prices_are_10_months_for_12_on_retail_tiers()
    {
        foreach (var tierId in new[] { SubscriptionTier.Basic, SubscriptionTier.Plus, SubscriptionTier.Premium })
        {
            var tier = TierCatalog.Get(tierId);
            var expectedAnnual = tier.MonthlyPrice.Amount * 10L;
            Assert.Equal(expectedAnnual, tier.AnnualPrice.Amount);
        }
    }

    [Fact]
    public void Sibling_first_and_second_are_14900_agorot()
    {
        Assert.Equal(14_900L, TierCatalog.SiblingMonthlyPrice(1).Amount);
        Assert.Equal(14_900L, TierCatalog.SiblingMonthlyPrice(2).Amount);
    }

    [Fact]
    public void Sibling_third_plus_are_9900_agorot()
    {
        Assert.Equal(9_900L, TierCatalog.SiblingMonthlyPrice(3).Amount);
        Assert.Equal(9_900L, TierCatalog.SiblingMonthlyPrice(4).Amount);
        Assert.Equal(9_900L, TierCatalog.SiblingMonthlyPrice(10).Amount);
    }

    [Fact]
    public void Sibling_ordinal_zero_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TierCatalog.SiblingMonthlyPrice(0));
    }

    [Fact]
    public void Premium_has_parent_dashboard_arabic_parity_tutor_handoff_priority_support()
    {
        var flags = TierCatalog.Get(SubscriptionTier.Premium).Features;
        Assert.True(flags.ParentDashboard);
        Assert.True(flags.ArabicDashboard);
        Assert.True(flags.TutorHandoffPdf);
        Assert.True(flags.PrioritySupport);
    }

    [Fact]
    public void Basic_and_Plus_have_no_parent_dashboard()
    {
        Assert.False(TierCatalog.Get(SubscriptionTier.Basic).Features.ParentDashboard);
        Assert.False(TierCatalog.Get(SubscriptionTier.Plus).Features.ParentDashboard);
    }

    [Fact]
    public void SchoolSku_has_parent_dashboard_feature_fenced_off()
    {
        var flags = TierCatalog.Get(SubscriptionTier.SchoolSku).Features;
        Assert.False(flags.ParentDashboard);
        Assert.False(flags.TutorHandoffPdf);
        Assert.True(flags.ClassroomDashboard);
        Assert.True(flags.TeacherAssignedPractice);
        Assert.True(flags.Sso);
    }

    [Fact]
    public void Basic_has_zero_photo_diagnostics()
    {
        var caps = TierCatalog.Get(SubscriptionTier.Basic).Caps;
        Assert.Equal(0, caps.PhotoDiagnosticsPerMonth);
        Assert.Equal(0, caps.PhotoDiagnosticsHardCapPerMonth);
    }

    [Fact]
    public void Premium_has_100_soft_300_hard_diagnostic_cap()
    {
        var caps = TierCatalog.Get(SubscriptionTier.Premium).Caps;
        Assert.Equal(100, caps.PhotoDiagnosticsPerMonth);
        Assert.Equal(300, caps.PhotoDiagnosticsHardCapPerMonth);
    }

    [Fact]
    public void RetailTiers_collection_is_in_display_order_basic_plus_premium()
    {
        Assert.Collection(
            TierCatalog.RetailTiers,
            t => Assert.Equal(SubscriptionTier.Basic, t.Tier),
            t => Assert.Equal(SubscriptionTier.Plus, t.Tier),
            t => Assert.Equal(SubscriptionTier.Premium, t.Tier));
    }
}
