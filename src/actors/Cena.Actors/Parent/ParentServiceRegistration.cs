// =============================================================================
// Cena Platform — ParentServiceRegistration (prr-009 / EPIC-PRR-C prod)
//
// Two composition modes, mirroring SubscriptionServiceRegistration and
// StudentPlanServiceRegistration:
//
//   AddParentChildBindingInMemory(services) — dev/test default.
//   AddParentChildBindingMarten(services)   — production binding.
//
// Callers that previously relied on the ad-hoc TryAddSingleton in
// CenaAdminServiceRegistration continue to work; CenaAdminServiceRegistration
// now delegates to AddParentChildBindingInMemory() for the test default,
// and production Hosts call AddParentChildBindingMarten() to replace the
// binding — per memory "No stubs — production grade" (2026-04-11).
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Parent;

/// <summary>DI helpers for parent-child binding registration.</summary>
public static class ParentServiceRegistration
{
    /// <summary>
    /// Register the in-memory <see cref="IParentChildBindingStore"/>
    /// binding. Suitable for tests and single-process dev runs. Idempotent
    /// via TryAdd.
    /// </summary>
    public static IServiceCollection AddParentChildBindingInMemory(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IParentChildBindingStore, InMemoryParentChildBindingStore>();
        return services;
    }

    /// <summary>
    /// Register the Marten-backed <see cref="MartenParentChildBindingStore"/>
    /// as the production binding for <see cref="IParentChildBindingStore"/>,
    /// and register the <see cref="ParentChildBindingDocument"/> schema on
    /// the configured Marten <see cref="StoreOptions"/>.
    /// <para>
    /// Production composition roots call <see cref="AddParentChildBindingInMemory"/>
    /// first (or rely on the existing TryAdd in <c>CenaAdminServiceRegistration</c>)
    /// then call this method to replace the in-memory binding. Requires
    /// <c>AddMarten()</c> to have been invoked on <paramref name="services"/>
    /// already (composition-root convention).
    /// </para>
    /// </summary>
    public static IServiceCollection AddParentChildBindingMarten(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MartenParentChildBindingStore>();
        services.Replace(ServiceDescriptor.Singleton<IParentChildBindingStore>(sp
            => sp.GetRequiredService<MartenParentChildBindingStore>()));
        services.ConfigureMarten(opts => opts.RegisterParentChildBindingContext());
        return services;
    }
}

/// <summary>Marten schema registration for the parent-child binding document.</summary>
public static class ParentChildBindingMartenRegistration
{
    /// <summary>
    /// Register <see cref="ParentChildBindingDocument"/> with Marten.
    /// Identity is the string Id property (pipe-joined triple).
    /// </summary>
    public static void RegisterParentChildBindingContext(this StoreOptions opts)
    {
        opts.Schema.For<ParentChildBindingDocument>()
            .Identity(d => d.Id);
    }
}
