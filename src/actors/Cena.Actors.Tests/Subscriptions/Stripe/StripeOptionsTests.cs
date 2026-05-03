// =============================================================================
// Cena Platform — StripeOptions config tests (EPIC-PRR-I PRR-301)
// =============================================================================

using Cena.Actors.Subscriptions.Stripe;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions.Stripe;

public class StripeOptionsTests
{
    [Fact]
    public void Binds_from_configuration_section()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_abc",
                ["Stripe:WebhookSigningSecret"] = "whsec_def",
                ["Stripe:PriceIds:BasicMonthly"] = "price_bm",
                ["Stripe:PriceIds:BasicAnnual"] = "price_ba",
                ["Stripe:PriceIds:PlusMonthly"] = "price_pm",
                ["Stripe:PriceIds:PlusAnnual"] = "price_pa",
                ["Stripe:PriceIds:PremiumMonthly"] = "price_prm",
                ["Stripe:PriceIds:PremiumAnnual"] = "price_pra",
                ["Stripe:SuccessUrl"] = "https://x/ok",
                ["Stripe:CancelUrl"] = "https://x/cancel",
            })
            .Build();

        var options = config.GetSection(StripeOptions.SectionName).Get<StripeOptions>()!;

        Assert.True(options.IsConfigured);
        Assert.Equal("sk_test_abc", options.SecretKey);
        Assert.Equal("price_bm", options.PriceIds.BasicMonthly);
        Assert.Equal("https://x/ok", options.SuccessUrl);
    }

    [Fact]
    public void Incomplete_configuration_is_not_configured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_abc",
                // missing signing secret and price ids
            })
            .Build();
        var options = config.GetSection(StripeOptions.SectionName).Get<StripeOptions>()
                      ?? new StripeOptions();
        Assert.False(options.IsConfigured);
    }
}
