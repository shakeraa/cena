// =============================================================================
// Cena Platform — StripePriceResolver (EPIC-PRR-I PRR-301)
//
// Pure mapping from (tier, cycle) → Stripe price id, backed by the configured
// StripeOptions. Unknown combinations throw; callers must have validated
// tier/cycle via the domain's SubscriptionCommands before reaching here.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Resolve Stripe price ids from tier + cycle. Throws
/// <see cref="InvalidOperationException"/> if the required price is not
/// configured — composition root should refuse to register the Stripe
/// adapter if <see cref="StripeOptions.IsConfigured"/> is false.
/// </summary>
public sealed class StripePriceResolver
{
    private readonly StripePriceIdMap _priceIds;

    public StripePriceResolver(StripeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _priceIds = options.PriceIds;
    }

    /// <summary>Resolve the Stripe price id for the given tier + cycle.</summary>
    public string Resolve(SubscriptionTier tier, BillingCycle cycle)
    {
        var id = (tier, cycle) switch
        {
            (SubscriptionTier.Basic, BillingCycle.Monthly) => _priceIds.BasicMonthly,
            (SubscriptionTier.Basic, BillingCycle.Annual) => _priceIds.BasicAnnual,
            (SubscriptionTier.Plus, BillingCycle.Monthly) => _priceIds.PlusMonthly,
            (SubscriptionTier.Plus, BillingCycle.Annual) => _priceIds.PlusAnnual,
            (SubscriptionTier.Premium, BillingCycle.Monthly) => _priceIds.PremiumMonthly,
            (SubscriptionTier.Premium, BillingCycle.Annual) => _priceIds.PremiumAnnual,
            _ => throw new InvalidOperationException(
                $"No Stripe price configured for tier={tier}, cycle={cycle}."),
        };

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException(
                $"Stripe price for ({tier}, {cycle}) is empty — check Stripe:PriceIds configuration.");
        }
        return id;
    }
}
