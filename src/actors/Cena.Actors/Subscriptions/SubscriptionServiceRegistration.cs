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
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // PRR-306 production refund-usage probe: aggregates Marten-backed
        // photo usage + raw hint events across linked students.
        services.Replace(ServiceDescriptor.Singleton<IRefundUsageProbe, MartenRefundUsageProbe>());
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
        // Checkout-session provider: sandbox by default. StripeServiceRegistration
        // .AddStripeCheckoutIfConfigured() replaces it when Stripe config is present.
        if (!services.Any(d => d.ServiceType == typeof(ICheckoutSessionProvider)))
        {
            services.AddSingleton<ICheckoutSessionProvider, SandboxCheckoutSessionProvider>();
        }

        // PRR-306 refund workflow composition:
        //   - Default refund gateway = sandbox. StripeServiceRegistration
        //     replaces this with StripeRefundGatewayService when Stripe
        //     config is present.
        //   - Usage probe default = noop (policy denies only on explicit
        //     abuse signals; production replaces with MartenRefundUsageProbe).
        //   - Original charge lookup walks the event stream so any host
        //     with ISubscriptionAggregateStore wired gets correct behaviour.
        //   - RefundService is the orchestrator; endpoints resolve it.
        if (!services.Any(d => d.ServiceType == typeof(IRefundGatewayService)))
        {
            services.AddSingleton<IRefundGatewayService, SandboxRefundGatewayService>();
        }
        if (!services.Any(d => d.ServiceType == typeof(IRefundUsageProbe)))
        {
            services.AddSingleton<IRefundUsageProbe, NoopRefundUsageProbe>();
        }
        if (!services.Any(d => d.ServiceType == typeof(IOriginalChargeLookup)))
        {
            services.AddSingleton<IOriginalChargeLookup, EventStreamOriginalChargeLookup>();
        }
        services.AddSingleton<RefundService>();
    }
}
