// =============================================================================
// Cena Platform — StripeServiceRegistration (EPIC-PRR-I PRR-301)
//
// Composition-root helper. Binds StripeOptions from config, registers the
// Stripe adapter as ICheckoutSessionProvider (replacing any prior default
// sandbox provider), and registers the webhook handler + dedup log.
//
// Call AddSubscriptionsMarten() FIRST so the subscription aggregate store is
// registered; this method's registrations will then compose against it.
// =============================================================================

using Cena.Actors.Subscriptions;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>DI helpers for wiring Stripe into the Student API host.</summary>
public static class StripeServiceRegistration
{
    /// <summary>
    /// Register Stripe if the configuration section is fully populated;
    /// returns <c>true</c> if Stripe was wired, <c>false</c> if the section
    /// was incomplete and the caller should keep the sandbox provider.
    /// Composition root checks the return value; a production environment
    /// without complete Stripe config should log a critical error (or fail
    /// startup if this env requires payments).
    /// </summary>
    public static bool AddStripeCheckoutIfConfigured(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(StripeOptions.SectionName);
        var options = section.Get<StripeOptions>() ?? new StripeOptions();
        if (!options.IsConfigured)
        {
            return false;
        }

        services.AddSingleton(options);
        services.AddSingleton<StripePriceResolver>();

        // Replace the default sandbox checkout provider with the Stripe one.
        services.RemoveAll<ICheckoutSessionProvider>();
        services.AddSingleton<ICheckoutSessionProvider, StripeCheckoutSessionProvider>();

        // Webhook dedup is persisted via Marten so Stripe retries after a pod
        // restart are deduped correctly. In-memory dedup would lose the seen-id
        // set on every restart, re-processing retries within Stripe's 3-day
        // replay window — duplicate subscription activations on the student's
        // stream, duplicate billing-cycle advances, and worst-case a double
        // refund on retries of `charge.refunded`. Per memory "No stubs —
        // production grade" (2026-04-11). Requires AddMarten() upstream.
        services.AddSingleton<IProcessedWebhookLog, MartenProcessedWebhookLog>();
        services.ConfigureMarten(opts =>
            opts.Schema.For<ProcessedWebhookDocument>().Identity(d => d.Id));
        services.AddSingleton<StripeWebhookHandler>();

        // PRR-306 refund gateway: replace the sandbox default with Stripe's
        // adapter so self-service refunds within the 30-day window actually
        // credit the card via the Stripe Refund API. The sandbox default
        // (registered in SubscriptionServiceRegistration) is kept for hosts
        // that never call AddStripeCheckoutIfConfigured (unit test setups).
        services.RemoveAll<IRefundGatewayService>();
        services.AddSingleton<IRefundGatewayService, StripeRefundGatewayService>();

        // Per-user discount-codes: replace the InMemory coupon provider
        // (registered in SubscriptionServiceRegistration) with the Stripe
        // adapter so admin-issued discounts mint real Stripe Coupons +
        // PromotionCodes.
        services.RemoveAll<IDiscountCouponProvider>();
        services.AddSingleton<IDiscountCouponProvider, StripeDiscountCouponProvider>();
        return true;
    }

    /// <summary>Remove all existing registrations of a service type (for replacement).</summary>
    private static void RemoveAll<TService>(this IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService))
            {
                services.RemoveAt(i);
            }
        }
    }
}
