// =============================================================================
// Cena Platform — DI extensions for Mashov sync integration (prr-039)
//
// Registers IMashovSyncCircuitBreaker, IMashovStalenessService, and the
// IMashovHealthClient + IMashovProbeTenantSource seams. The probe
// background service is registered when the host explicitly opts in via
// `AddMashovSyncHealthProbe` — Launch posture is "don't run the probe
// until at least one tenant has Mashov credentials in ISecretStore".
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Integrations.Mashov;

public static class MashovSyncRegistration
{
    /// <summary>
    /// Register the circuit breaker + staleness service + default probe
    /// seams. Idempotent — callers that already registered a real
    /// <see cref="IMashovHealthClient"/> or <see cref="IMashovProbeTenantSource"/>
    /// keep their registration.
    /// </summary>
    public static IServiceCollection AddMashovSyncCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IMashovSyncCircuitBreaker, MashovSyncCircuitBreaker>();
        services.TryAddSingleton<IMashovStalenessService, MashovStalenessService>();
        services.TryAddSingleton<IMashovProbeTenantSource, NullMashovProbeTenantSource>();
        return services;
    }

    /// <summary>
    /// Register the synthetic probe as a background service. Caller must
    /// have registered a real <see cref="IMashovHealthClient"/>.
    /// </summary>
    public static IServiceCollection AddMashovSyncHealthProbe(this IServiceCollection services)
    {
        services.AddMashovSyncCore();
        services.AddHostedService<MashovSyncHealthProbe>();
        return services;
    }
}
