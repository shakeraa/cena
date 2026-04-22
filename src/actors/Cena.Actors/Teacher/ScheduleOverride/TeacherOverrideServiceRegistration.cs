// =============================================================================
// Cena Platform — TeacherOverrideAggregate DI registration (prr-150)
//
// Single entry point for wiring TeacherOverride primitives into a DI
// container. Idempotent via TryAdd — safe to call from multiple Host
// compositions (Actors.Host, Admin.Api.Host).
//
// Registered (all singletons):
//   - ITeacherOverrideStore             -> InMemoryTeacherOverrideStore
//   - IStudentInstituteLookup           -> InMemoryStudentInstituteLookup
//     (admin host replaces with the Marten-backed concrete post-registration)
//   - TeacherOverrideCommands           -> itself
//   - IOverrideAwareSchedulerInputsBridge -> OverrideAwareSchedulerInputsBridge
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Extension methods to register TeacherOverride services.
/// </summary>
public static class TeacherOverrideServiceRegistration
{
    /// <summary>
    /// Register the TeacherOverride primitives: store, institute lookup,
    /// commands, and scheduler bridge. Idempotent.
    /// </summary>
    public static IServiceCollection AddTeacherOverrideServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ITeacherOverrideStore, InMemoryTeacherOverrideStore>();
        services.TryAddSingleton<IStudentInstituteLookup>(
            sp => new InMemoryStudentInstituteLookup());
        services.TryAddSingleton<TeacherOverrideCommands>();
        services.TryAddSingleton<IOverrideAwareSchedulerInputsBridge, OverrideAwareSchedulerInputsBridge>();

        return services;
    }

    /// <summary>
    /// Replace the in-memory <see cref="ITeacherOverrideStore"/> binding
    /// registered by <see cref="AddTeacherOverrideServices"/> with the
    /// Marten-backed <see cref="MartenTeacherOverrideStore"/>, and
    /// register every teacher-override event type on the Marten
    /// <see cref="StoreOptions"/> via
    /// <see cref="TeacherOverrideMartenRegistration.RegisterTeacherOverrideContext"/>.
    /// <para>
    /// Per memory "No stubs — production grade" (2026-04-11), the in-memory
    /// store is test-only; production hosts persist the
    /// TeacherOverrideAggregate event stream via Marten so motivation-
    /// profile overrides, budget adjustments, and pinned topics survive a
    /// pod restart. Without this, every deploy silently reverts every
    /// teacher override — a trust erosion at scale.
    /// </para>
    /// <para>
    /// Composition order: call <see cref="AddTeacherOverrideServices"/>
    /// first (shared services + in-memory default), then this method to
    /// replace the store binding. Requires <c>AddMarten()</c> to have
    /// been invoked on <paramref name="services"/> already.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTeacherOverrideMarten(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MartenTeacherOverrideStore>();
        services.Replace(ServiceDescriptor.Singleton<ITeacherOverrideStore>(sp
            => sp.GetRequiredService<MartenTeacherOverrideStore>()));
        services.ConfigureMarten(opts => opts.RegisterTeacherOverrideContext());
        return services;
    }
}
