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

    // --- B2B School SKU volume pricing (PRR-341) ------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(499)]
    public void SchoolSku_small_bracket_is_3500_agorot_per_student(int count)
    {
        Assert.Equal(3_500L, TierCatalog.SchoolSkuMonthlyPricePerStudent(count).Amount);
        Assert.Equal("small", TierCatalog.SchoolSkuVolumeBracket(count));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(1_499)]
    public void SchoolSku_mid_bracket_is_2900_agorot_per_student(int count)
    {
        Assert.Equal(2_900L, TierCatalog.SchoolSkuMonthlyPricePerStudent(count).Amount);
        Assert.Equal("mid", TierCatalog.SchoolSkuVolumeBracket(count));
    }

    [Theory]
    [InlineData(1_500)]
    [InlineData(5_000)]
    [InlineData(25_000)]
    public void SchoolSku_large_bracket_is_2400_agorot_per_student(int count)
    {
        Assert.Equal(2_400L, TierCatalog.SchoolSkuMonthlyPricePerStudent(count).Amount);
        Assert.Equal("large", TierCatalog.SchoolSkuVolumeBracket(count));
    }

    [Fact]
    public void SchoolSku_bracket_is_step_function_no_interpolation_between_brackets()
    {
        // Exact boundaries: 499 = small, 500 = mid, 1499 = mid, 1500 = large.
        // Lock the step-function behaviour so no well-meaning refactor
        // turns this into a linear-blend that would fabricate a price
        // neither the sales team nor the pricing page publishes.
        Assert.Equal(3_500L, TierCatalog.SchoolSkuMonthlyPricePerStudent(499).Amount);
        Assert.Equal(2_900L, TierCatalog.SchoolSkuMonthlyPricePerStudent(500).Amount);
        Assert.Equal(2_900L, TierCatalog.SchoolSkuMonthlyPricePerStudent(1_499).Amount);
        Assert.Equal(2_400L, TierCatalog.SchoolSkuMonthlyPricePerStudent(1_500).Amount);
    }

    [Fact]
    public void SchoolSku_monotonically_non_increasing_in_volume()
    {
        // Volume discount invariant — price-per-student must never go UP
        // as student count grows. Lock this so a bracket-table typo
        // (e.g., swapping mid and large rates) is caught in CI.
        long prev = long.MaxValue;
        foreach (var count in new[] { 1, 50, 99, 100, 250, 499, 500, 750, 1_499, 1_500, 5_000, 25_000 })
        {
            var price = TierCatalog.SchoolSkuMonthlyPricePerStudent(count).Amount;
            Assert.True(
                price <= prev,
                $"SchoolSku pricing must be non-increasing in volume; at {count} students price jumped from {prev} to {price}.");
            prev = price;
        }
    }

    [Theory]
    [InlineData(100, 100 * 3_500L)]
    [InlineData(500, 500 * 2_900L)]
    [InlineData(1_500, 1_500 * 2_400L)]
    [InlineData(2_000, 2_000 * 2_400L)]
    public void SchoolSku_contract_total_equals_per_student_times_seats(
        int count, long expectedTotalAgorot)
    {
        var total = TierCatalog.SchoolSkuMonthlyContractTotal(count);
        Assert.Equal(expectedTotalAgorot, total.Amount);
    }

    [Fact]
    public void SchoolSku_volume_pricing_rejects_zero_and_negative_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TierCatalog.SchoolSkuMonthlyPricePerStudent(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TierCatalog.SchoolSkuMonthlyPricePerStudent(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TierCatalog.SchoolSkuMonthlyContractTotal(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TierCatalog.SchoolSkuVolumeBracket(0));
    }

    [Fact]
    public void SchoolSku_entry_bracket_matches_single_seat_anchor_on_TierDefinition()
    {
        // The single-seat anchor on TierCatalog.Get(SchoolSku).MonthlyPrice
        // is the canonical entry-bracket rate (₪35/student/mo). Volume
        // pricing extends downward from this; the anchor stays the same
        // so existing call-sites that treat SchoolSku as a flat tier do
        // not silently migrate to the volume-discounted rate.
        var anchor = TierCatalog.Get(SubscriptionTier.SchoolSku).MonthlyPrice.Amount;
        var smallBracketPerStudent = TierCatalog
            .SchoolSkuMonthlyPricePerStudent(100).Amount;
        Assert.Equal(anchor, smallBracketPerStudent);
    }
}
