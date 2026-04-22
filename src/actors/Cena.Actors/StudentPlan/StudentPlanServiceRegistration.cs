// =============================================================================
// Cena Platform — StudentPlanAggregate DI registration (prr-218, supersedes prr-148)
//
// Two composition modes, mirroring SubscriptionServiceRegistration:
//   AddStudentPlanServices(services)      — InMemory store (test + dev default)
//   AddStudentPlanMarten(services)        — Marten-backed store (production)
//
// Production composition roots call both in order so the Marten binding
// wins on resolve: AddStudentPlanServices() first for the shared services
// (command handler, reader, migration infra, catalog validator), then
// AddStudentPlanMarten() to replace the in-memory store with the
// Marten-backed implementation per memory "No stubs — production grade"
// (2026-04-11). Tests call AddStudentPlanServices() alone and retain the
// in-memory behaviour.
//
// Registered by AddStudentPlanServices:
//   - IStudentPlanAggregateStore    -> InMemoryStudentPlanAggregateStore
//   - IMigrationMarkerStore         -> InMemoryStudentPlanAggregateStore
//   - IStudentPlanInputsService     -> StudentPlanInputsService (legacy
//     projection for the scheduler bridge, prr-149).
//   - IStudentPlanReader            -> StudentPlanReader (multi-target
//     read side for /api/me/exam-targets endpoints, prr-218).
//   - IStudentPlanCommandHandler    -> StudentPlanCommandHandler
//     (invariant-enforcing command surface, prr-218).
//   - IClassroomTargetAssignmentService (prr-236), IQuestionPaperCatalogValidator
//     (prr-243 default), migration services (prr-219).
//
// Registered (overridden) by AddStudentPlanMarten:
//   - IStudentPlanAggregateStore    -> MartenStudentPlanAggregateStore
//   - IMigrationMarkerStore         -> MartenStudentPlanAggregateStore
//   - StoreOptions.Events.*         -> every StudentPlan event type
//     (via ConfigureMarten + StudentPlanMartenRegistration.RegisterStudentPlanContext).
// =============================================================================

using Cena.Actors.StudentPlan.Migration;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Extension methods to register StudentPlanAggregate services.
/// </summary>
public static class StudentPlanServiceRegistration
{
    /// <summary>
    /// Register the StudentPlanAggregate primitives with the in-memory
    /// aggregate store as the default binding. Suitable for tests and
    /// single-process dev runs. Production hosts MUST call
    /// <see cref="AddStudentPlanMarten"/> after this so the Marten-backed
    /// store replaces the in-memory default — per memory "No stubs —
    /// production grade" (2026-04-11). Idempotent via TryAdd.
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

        // PRR-236: Classroom-assigned target teacher service. Fans out a
        // single teacher AssignClassroomTargetCommand to every enrolled
        // student via the StudentPlan command handler above. Requires an
        // IClassroomRosterLookup, which each host registers separately
        // (Marten-backed in Admin.Api.Host, in-memory in tests).
        services.TryAddSingleton<IClassroomTargetAssignmentService,
            ClassroomTargetAssignmentService>();

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

    /// <summary>
    /// Register the Marten-backed <see cref="MartenStudentPlanAggregateStore"/>
    /// as the production binding for <see cref="IStudentPlanAggregateStore"/>
    /// and <see cref="IMigrationMarkerStore"/>, and register every
    /// StudentPlan event type with the Marten <see cref="StoreOptions"/>
    /// via <see cref="StudentPlanMartenRegistration.RegisterStudentPlanContext"/>.
    /// <para>
    /// Call order in composition roots: <c>AddStudentPlanServices()</c>
    /// first (shared services + in-memory fallback), then
    /// <c>AddStudentPlanMarten()</c> to replace the in-memory aggregate
    /// store with the Marten implementation. Requires <c>AddMarten()</c>
    /// to have been called on <paramref name="services"/> (composition-root
    /// convention; <see cref="ConfigureMarten"/> is invoked here so the
    /// StudentPlan event registration attaches to whatever store is
    /// configured).
    /// </para>
    /// <para>
    /// The override is achieved by calling <see cref="ServiceCollectionDescriptorExtensions.Replace"/>
    /// for each re-bound service. The in-memory store registration from
    /// <see cref="AddStudentPlanServices"/> is retained as a concrete
    /// singleton so existing tests that resolve <c>InMemoryStudentPlanAggregateStore</c>
    /// directly still compile; DI resolutions via the interfaces now
    /// return the Marten-backed implementation.
    /// </para>
    /// </summary>
    public static IServiceCollection AddStudentPlanMarten(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Concrete Marten store (singleton). A concrete registration is
        // kept separately so both interfaces below resolve to the SAME
        // instance (important: IMigrationMarkerStore and
        // IStudentPlanAggregateStore share state, so two separate
        // instances would silently desynchronise the marker-scan path).
        services.TryAddSingleton<MartenStudentPlanAggregateStore>();

        // Replace the in-memory interface bindings with the Marten store.
        // Replace (not Add) so there is no duplicate-registration ambiguity
        // downstream — resolution is deterministic regardless of
        // registration order.
        services.Replace(ServiceDescriptor.Singleton<IStudentPlanAggregateStore>(sp
            => sp.GetRequiredService<MartenStudentPlanAggregateStore>()));
        services.Replace(ServiceDescriptor.Singleton<IMigrationMarkerStore>(sp
            => sp.GetRequiredService<MartenStudentPlanAggregateStore>()));

        // Register StudentPlan event types on the Marten StoreOptions so
        // deserialisation resolves the payload JSON back to the concrete
        // record type without an assembly-scan fallback.
        services.ConfigureMarten(opts => opts.RegisterStudentPlanContext());

        return services;
    }
}
