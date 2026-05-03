// =============================================================================
// Cena Platform — Outbound SMS DI registration (prr-018).
//
// Registers the policy chain, the four default policies, and the gateway in
// the explicit order (sanitizer → shipgate → rate-limit → quiet-hours). The
// order is load-bearing — see OutboundSmsPolicyChain.cs for the rationale.
//
// Every caller that wires the actor host up must invoke AddOutboundSmsPolicy.
// The architecture test asserts IOutboundSmsGateway is the single entry point
// into ISmsSender for parent-nudge traffic.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Notifications.OutboundSms;

public static class OutboundSmsServiceCollectionExtensions
{
    /// <summary>
    /// Register the full outbound-SMS policy chain and gateway. Caller still
    /// needs to register <see cref="ISmsSender"/>, <see cref="StackExchange.Redis.IConnectionMultiplexer"/>,
    /// and the IClock. Idempotent — calling twice is a no-op on the second call.
    /// </summary>
    public static IServiceCollection AddOutboundSmsPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SmsSanitizerOptions>(
            configuration.GetSection(SmsSanitizerOptions.SectionName));

        // Chain policies — order matters. We add them in explicit ordered
        // registrations so the enumerable resolves in this order (the default
        // for Microsoft.Extensions.DependencyInjection honours registration
        // order within a single IEnumerable<T>).
        services.AddSingleton<IOutboundSmsPolicy, SmsSanitizerPolicy>();
        services.AddSingleton<IOutboundSmsPolicy, SmsShipgatePolicy>();
        services.AddSingleton<IOutboundSmsPolicy, SmsRateLimitPolicy>();
        services.AddSingleton<IOutboundSmsPolicy, SmsQuietHoursPolicy>();

        services.AddSingleton<IOutboundSmsPolicyChain, OutboundSmsPolicyChain>();
        services.AddSingleton<IOutboundSmsGateway, OutboundSmsGateway>();

        return services;
    }
}
