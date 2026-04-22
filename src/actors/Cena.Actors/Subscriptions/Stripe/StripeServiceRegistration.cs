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

        // Webhook dedup is in-memory at v1; production swap-out is a Marten
        // or Redis-backed store (interface is narrow — mechanical).
        services.AddSingleton<IProcessedWebhookLog, InMemoryProcessedWebhookLog>();
        services.AddSingleton<StripeWebhookHandler>();
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
