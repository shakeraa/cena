// =============================================================================
// Cena Platform — StudentPlanAggregate DI registration (prr-148)
//
// Single entry point for wiring the StudentPlanAggregate primitives into a
// DI container. Idempotent via TryAdd — safe to call from multiple Host
// compositions (Actors.Host, Student.Api.Host, Admin.Api.Host).
//
// Registered:
//   - IStudentPlanAggregateStore -> InMemoryStudentPlanAggregateStore
//     (singleton; Marten-backed overlay is a follow-up).
//   - IStudentPlanInputsService  -> StudentPlanInputsService (singleton).
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Extension methods to register StudentPlanAggregate services.
/// </summary>
public static class StudentPlanServiceRegistration
{
    /// <summary>
    /// Register the StudentPlanAggregate primitives: store + inputs read
    /// service. Idempotent.
    /// </summary>
    public static IServiceCollection AddStudentPlanServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IStudentPlanAggregateStore, InMemoryStudentPlanAggregateStore>();
        services.TryAddSingleton<IStudentPlanInputsService, StudentPlanInputsService>();

        return services;
    }
}
