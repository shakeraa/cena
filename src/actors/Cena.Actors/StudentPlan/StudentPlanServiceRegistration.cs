// =============================================================================
// Cena Platform — StudentPlanAggregate DI registration (prr-218, supersedes prr-148)
//
// Single entry point for wiring the multi-target StudentPlanAggregate
// primitives into a DI container. Idempotent via TryAdd — safe to call
// from multiple Host compositions (Actors.Host, Student.Api.Host,
// Admin.Api.Host).
//
// Registered:
//   - IStudentPlanAggregateStore    -> InMemoryStudentPlanAggregateStore
//     (singleton; Marten-backed overlay is a follow-up).
//   - IStudentPlanInputsService     -> StudentPlanInputsService (legacy
//     projection for the scheduler bridge, prr-149).
//   - IStudentPlanReader            -> StudentPlanReader (multi-target
//     read side for /api/me/exam-targets endpoints, prr-218).
//   - IStudentPlanCommandHandler    -> StudentPlanCommandHandler
//     (invariant-enforcing command surface, prr-218).
// =============================================================================

using Cena.Actors.StudentPlan.Migration;
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
    /// service + multi-target reader + command handler + migration
    /// safety-net services. Idempotent.
    /// </summary>
    public static IServiceCollection AddStudentPlanServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryStudentPlanAggregateStore>();
        services.TryAddSingleton<IStudentPlanAggregateStore>(sp
            => sp.GetRequiredService<InMemoryStudentPlanAggregateStore>());
        services.TryAddSingleton<IMigrationMarkerStore>(sp
            => sp.GetRequiredService<InMemoryStudentPlanAggregateStore>());
        services.TryAddSingleton<IStudentPlanInputsService, StudentPlanInputsService>();
        services.TryAddSingleton<IStudentPlanReader, StudentPlanReader>();
        services.TryAddSingleton<IStudentPlanCommandHandler, StudentPlanCommandHandler>();

        // PRR-243: permissive catalog validator by default (accepts any
        // non-empty paper code). The Student API host overrides this with
        // CatalogBackedQuestionPaperCatalogValidator, which consults the
        // loaded YAML catalog snapshot.
        services.TryAddSingleton<IQuestionPaperCatalogValidator>(
            AllowAllQuestionPaperCatalogValidator.Instance);

        // Migration safety net (prr-219). Feature flag defaults off via
        // MigrationFeatureFlagSnapshot.Off; hosts override the provider
        // delegate to wire real config.
        services.TryAddSingleton<IMigrationFeatureFlag>(_
            => new MigrationFeatureFlag(() => MigrationFeatureFlagSnapshot.Off));
        services.TryAddSingleton<IStudentPlanMigrationService, StudentPlanMigrationService>();

        return services;
    }
}
