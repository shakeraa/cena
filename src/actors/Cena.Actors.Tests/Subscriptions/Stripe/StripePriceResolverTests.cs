// =============================================================================
// Cena Platform — StripePriceResolver tests (EPIC-PRR-I PRR-301)
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Stripe;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions.Stripe;

public class StripePriceResolverTests
{
    private readonly StripeOptions _fullyConfigured = new()
    {
        SecretKey = "sk_test_xxx",
        WebhookSigningSecret = "whsec_xxx",
        PriceIds = new StripePriceIdMap
        {
            BasicMonthly = "price_basic_m",
            BasicAnnual = "price_basic_a",
            PlusMonthly = "price_plus_m",
            PlusAnnual = "price_plus_a",
            PremiumMonthly = "price_premium_m",
            PremiumAnnual = "price_premium_a",
        },
    };

    [Theory]
    [InlineData(SubscriptionTier.Basic, BillingCycle.Monthly, "price_basic_m")]
    [InlineData(SubscriptionTier.Basic, BillingCycle.Annual, "price_basic_a")]
    [InlineData(SubscriptionTier.Plus, BillingCycle.Monthly, "price_plus_m")]
    [InlineData(SubscriptionTier.Plus, BillingCycle.Annual, "price_plus_a")]
    [InlineData(SubscriptionTier.Premium, BillingCycle.Monthly, "price_premium_m")]
    [InlineData(SubscriptionTier.Premium, BillingCycle.Annual, "price_premium_a")]
    public void Resolve_retail_tier_cycle_returns_configured_price_id(
        SubscriptionTier tier, BillingCycle cycle, string expected)
    {
        var resolver = new StripePriceResolver(_fullyConfigured);
        Assert.Equal(expected, resolver.Resolve(tier, cycle));
    }

    [Fact]
    public void Resolve_unsubscribed_throws()
    {
        var resolver = new StripePriceResolver(_fullyConfigured);
        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(SubscriptionTier.Unsubscribed, BillingCycle.Monthly));
    }

    [Fact]
    public void Resolve_none_cycle_throws()
    {
        var resolver = new StripePriceResolver(_fullyConfigured);
        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(SubscriptionTier.Basic, BillingCycle.None));
    }

    [Fact]
    public void Resolve_with_empty_price_id_throws()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_xxx",
            WebhookSigningSecret = "whsec_xxx",
            PriceIds = new StripePriceIdMap
            {
                BasicMonthly = "",   // missing
                BasicAnnual = "x",
                PlusMonthly = "x", PlusAnnual = "x",
                PremiumMonthly = "x", PremiumAnnual = "x",
            },
        };
        var resolver = new StripePriceResolver(options);
        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(SubscriptionTier.Basic, BillingCycle.Monthly));
    }

    [Fact]
    public void IsConfigured_false_when_any_price_missing()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk",
            WebhookSigningSecret = "whsec",
            PriceIds = new StripePriceIdMap { BasicMonthly = "x" },
        };
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_true_when_all_set()
    {
        Assert.True(_fullyConfigured.IsConfigured);
    }
}
