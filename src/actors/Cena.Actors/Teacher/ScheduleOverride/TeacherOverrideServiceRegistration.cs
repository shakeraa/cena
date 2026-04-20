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
}
