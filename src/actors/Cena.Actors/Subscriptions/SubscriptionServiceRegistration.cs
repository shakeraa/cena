// =============================================================================
// Cena Platform — SubscriptionServiceRegistration (EPIC-PRR-I, ADR-0057)
//
// DI wiring. Two composition modes:
//   AddSubscriptions(services)       — InMemory store (dev/test default)
//   AddSubscriptionsMarten(services) — Marten-backed store (prod)
//
// Both register the shared services (entitlement resolver, cap enforcer,
// routing policy, payment gateway). The gateway default is SandboxPayment;
// production composition overrides with Stripe/Bit/PayBox adapters.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Subscriptions;

/// <summary>DI registration helpers for the Subscriptions bounded context.</summary>
public static class SubscriptionServiceRegistration
{
    /// <summary>
    /// Register with the in-memory store. Suitable for dev/test and
    /// single-instance deployments (matches the ADR-0042 migration pattern
    /// where InMemory is the v1).
    /// </summary>
    public static IServiceCollection AddSubscriptions(this IServiceCollection services)
    {
        services.AddSingleton<ISubscriptionAggregateStore, InMemorySubscriptionAggregateStore>();
        AddSharedServices(services);
        return services;
    }

    /// <summary>
    /// Register with the Marten-backed store and register the Subscriptions
    /// Marten context (event types + entitlement projection) via
    /// <c>ConfigureMarten</c>. Requires <c>AddMarten</c> to have been called
    /// first (composition-root convention).
    /// </summary>
    public static IServiceCollection AddSubscriptionsMarten(this IServiceCollection services)
    {
        services.AddSingleton<ISubscriptionAggregateStore, MartenSubscriptionAggregateStore>();
        services.ConfigureMarten(opts => opts.RegisterSubscriptionsContext());
        AddSharedServices(services);
        return services;
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.AddSingleton<IStudentEntitlementResolver, StudentEntitlementResolver>();
        services.AddSingleton<IPerTierCapEnforcer, PerTierCapEnforcer>();
        services.AddSingleton<ITierLlmRoutingPolicy, TierLlmRoutingPolicy>();
        // Default gateway = sandbox. Production composition root overrides
        // by calling AddSingleton<IPaymentGateway, StripePaymentGateway>()
        // AFTER AddSubscriptions (last registration wins on resolve).
        if (!services.Any(d => d.ServiceType == typeof(IPaymentGateway)))
        {
            services.AddSingleton<IPaymentGateway, SandboxPaymentGateway>();
        }
    }
}
