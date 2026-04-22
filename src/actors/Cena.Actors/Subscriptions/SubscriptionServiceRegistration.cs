// =============================================================================
// Cena Platform — SubscriptionServiceRegistration (EPIC-PRR-I, ADR-0057)
//
// DI wiring. Matches ConsentServiceRegistration convention: a single
// AddSubscriptions() method the Program.cs composition root calls.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Subscriptions;

/// <summary>DI registration helpers for the Subscriptions bounded context.</summary>
public static class SubscriptionServiceRegistration
{
    /// <summary>
    /// Register the in-memory aggregate store as the <see cref="ISubscriptionAggregateStore"/>
    /// implementation. Override in later tasks with a Marten-backed variant
    /// once the durable-storage task lands (see ADR-0057 follow-ups).
    /// </summary>
    public static IServiceCollection AddSubscriptions(this IServiceCollection services)
    {
        services.AddSingleton<ISubscriptionAggregateStore, InMemorySubscriptionAggregateStore>();
        return services;
    }
}
