// =============================================================================
// Cena Platform — DI registration for k-anonymity enforcer (prr-026)
//
// Hosts call AddKAnonymityEnforcer() during startup to register:
//   - IKAnonymityEnforcer → KAnonymityEnforcer (singleton; ambient meter)
//
// The enforcer is a singleton because it holds only a Counter<long> handle
// and a logger. IMeterFactory is the same instance already registered by
// every host (via AddMetrics() / AddOpenTelemetry()).
//
// See src/shared/Cena.Infrastructure/Analytics/KAnonymityEnforcer.cs.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Infrastructure.Analytics;

/// <summary>
/// DI extensions for registering the prr-026 k-anonymity enforcer.
/// </summary>
public static class KAnonymityEnforcerRegistration
{
    /// <summary>
    /// Register <see cref="IKAnonymityEnforcer"/> → <see cref="KAnonymityEnforcer"/>
    /// as a singleton. Idempotent (uses TryAddSingleton) so hosts can call it
    /// once per composition root without worrying about double-registration.
    /// </summary>
    public static IServiceCollection AddKAnonymityEnforcer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IKAnonymityEnforcer, KAnonymityEnforcer>();
        return services;
    }
}
