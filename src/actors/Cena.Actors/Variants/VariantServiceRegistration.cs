// =============================================================================
// Cena Platform — Variant Generation DI registration (PRR-265, ADR-0059 §15.5)
//
// One-stop service registration for the variant rate-limit + gate
// composition. Hosts call AddVariantGenerationGate() once during startup;
// the registration is idempotent (TryAddSingleton) so repeated calls are
// safe.
//
// Composes:
//   - IVariantRateLimitPolicy → VariantRateLimitPolicy   (compile-time defaults)
//   - IVariantRateLimiter      → RedisVariantRateLimiter  (requires
//     IConnectionMultiplexer + IMeterFactory + ILogger; production hosts
//     register IConnectionMultiplexer in Program.cs)
//   - IVariantGenerationGate   → VariantGenerationGate    (composition)
//
// Test composition: AddVariantGenerationGateForTesting() swaps in
// NullVariantRateLimiter so unit tests don't require Redis. Production
// hosts MUST NOT call the test variant.
// =============================================================================

using Cena.Infrastructure.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Variants;

/// <summary>
/// DI extensions for the variant rate-limit + gate composition.
/// </summary>
public static class VariantServiceRegistration
{
    /// <summary>
    /// Register the production composition. Caller is responsible for
    /// having registered <c>IConnectionMultiplexer</c>, <c>IMeterFactory</c>,
    /// and <c>IStudentEntitlementResolver</c> first.
    /// </summary>
    public static IServiceCollection AddVariantGenerationGate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IVariantRateLimitPolicy, VariantRateLimitPolicy>();
        services.TryAddSingleton<IVariantRateLimiter, RedisVariantRateLimiter>();
        services.TryAddSingleton<IVariantGenerationGate, VariantGenerationGate>();
        return services;
    }

    /// <summary>
    /// Test-only composition: NullVariantRateLimiter (always allow) so
    /// unit tests don't require Redis. The policy + gate use their real
    /// implementations so the matrix is exercised.
    /// </summary>
    public static IServiceCollection AddVariantGenerationGateForTesting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IVariantRateLimitPolicy, VariantRateLimitPolicy>();
        services.TryAddSingleton<IVariantRateLimiter>(NullVariantRateLimiter.Instance);
        services.TryAddSingleton<IVariantGenerationGate, VariantGenerationGate>();
        return services;
    }
}
