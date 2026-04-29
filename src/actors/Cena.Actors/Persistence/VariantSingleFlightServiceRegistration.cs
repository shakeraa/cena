// =============================================================================
// Cena Platform — Variant Single-Flight Lock DI registration (PRR-260)
//
// One-stop registration for the cohort single-flight lock around variant
// generation. Hosts call AddVariantSingleFlightLock() once during startup;
// idempotent (TryAddSingleton) so repeated calls are safe.
//
// Production hosts MUST register IConnectionMultiplexer + IMeterFactory
// before calling. Test composition: AddVariantSingleFlightLockForTesting()
// uses the no-op implementation.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Persistence;

/// <summary>
/// DI extensions for the variant single-flight lock.
/// </summary>
public static class VariantSingleFlightServiceRegistration
{
    /// <summary>
    /// Register the production composition. Caller must have already
    /// registered <c>IConnectionMultiplexer</c> + <c>IMeterFactory</c>.
    /// </summary>
    public static IServiceCollection AddVariantSingleFlightLock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IVariantSingleFlightLock, RedisVariantSingleFlightLock>();
        return services;
    }

    /// <summary>
    /// Test-only composition: NullVariantSingleFlightLock (always run writer inline).
    /// Production hosts MUST NOT call this variant.
    /// </summary>
    public static IServiceCollection AddVariantSingleFlightLockForTesting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IVariantSingleFlightLock>(NullVariantSingleFlightLock.Instance);
        return services;
    }
}
