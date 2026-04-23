// =============================================================================
// Cena Platform — SessionContextServiceRegistration (EPIC-PRR-I PRR-310 slice 1)
//
// DI extension that wires the slice-1 session-context surface: the
// resolver that pins tier + caps + features at session start. Hosts call
// AddSessionContextResolution() after registering IStudentEntitlementResolver
// (which lives in the Subscriptions bounded context). TryAdd-guarded so
// re-registration is idempotent.
//
// Slice 2 (NATS envelope enrichment) and slice 3 (SessionActor pinning)
// consume ISessionContextResolver and are explicitly out of scope here;
// they will register their own services in follow-up tasks without
// touching this file.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Sessions;

/// <summary>DI helpers for the PRR-310 slice-1 session-context surface.</summary>
public static class SessionContextServiceRegistration
{
    /// <summary>
    /// Register the default <see cref="ISessionContextResolver"/> =
    /// <see cref="SessionContextResolver"/>. Requires
    /// <c>IStudentEntitlementResolver</c> to be registered upstream
    /// (Subscription composition does this).
    /// </summary>
    public static IServiceCollection AddSessionContextResolution(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISessionContextResolver, SessionContextResolver>();
        return services;
    }
}
