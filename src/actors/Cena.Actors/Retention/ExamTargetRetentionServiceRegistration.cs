// =============================================================================
// Cena Platform — ExamTarget retention DI wiring (prr-222/223/229)
//
// Single entry point for hosts. Registers:
//   - ISkillKeyedMasteryStore         → InMemorySkillKeyedMasteryStore
//   - IBktStateTracker                → BktStateTracker
//   - IExamTargetRetentionExtensionStore
//                                      → InMemoryExamTargetRetentionExtensionStore
//   - IArchivedExamTargetSource       → InMemoryArchivedExamTargetSource
//   - IRetentionShredNotifier         → NoopRetentionShredNotifier (replaced
//                                        by the real notifier in the Admin
//                                        / Student host compositions)
//   - IErasureProjectionCascade       → ExamTargetErasureCascade (added to
//                                        the IEnumerable<cascade> pool)
//   - ExamTargetRetentionMetrics      (singleton)
//   - ExamTargetRetentionWorker       (IHostedService)
//
// Idempotent via TryAdd. Safe to call from Actors.Host, Student.Api.Host,
// and Admin.Api.Host.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Rtbf;
using Cena.Infrastructure.Compliance;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Retention;

/// <summary>
/// DI extensions for the exam-target retention pipeline.
/// </summary>
public static class ExamTargetRetentionServiceRegistration
{
    /// <summary>
    /// Wire the prr-222 / prr-223 / prr-229 services into the container.
    /// </summary>
    public static IServiceCollection AddExamTargetRetentionServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISkillKeyedMasteryStore, InMemorySkillKeyedMasteryStore>();
        services.TryAddSingleton<IBktParameterProvider, DefaultBktParameterProvider>();
        services.TryAddSingleton<IBktStateTracker, BktStateTracker>();

        services.TryAddSingleton<
            IExamTargetRetentionExtensionStore,
            InMemoryExamTargetRetentionExtensionStore>();

        services.TryAddSingleton<InMemoryArchivedExamTargetSource>();
        services.TryAddSingleton<IArchivedExamTargetSource>(sp =>
            sp.GetRequiredService<InMemoryArchivedExamTargetSource>());

        services.TryAddSingleton<IRetentionShredNotifier, NoopRetentionShredNotifier>();

        services.TryAddSingleton<ExamTargetRetentionMetrics>();

        services.Configure<ExamTargetRetentionWorkerOptions>(_ => { });
        services.AddHostedService<ExamTargetRetentionWorker>();

        // Register the cascade in the IEnumerable<IErasureProjectionCascade>
        // pool consumed by RightToErasureService. Use plain AddSingleton
        // (NOT TryAdd) so multiple cascades can coexist in the pool —
        // RightToErasureService requests IEnumerable<T> and the container
        // returns every registration.
        services.AddSingleton<IErasureProjectionCascade, ExamTargetErasureCascade>();

        return services;
    }

    /// <summary>
    /// Replace the in-memory <see cref="ISkillKeyedMasteryStore"/>
    /// binding registered by <see cref="AddExamTargetRetentionServices"/>
    /// with the Marten-backed <see cref="MartenSkillKeyedMasteryStore"/>
    /// and register the <see cref="SkillKeyedMasteryDocument"/> schema
    /// via <see cref="MasteryMartenRegistration.RegisterMasteryContext"/>.
    /// <para>
    /// Production composition roots call <see cref="AddExamTargetRetentionServices"/>
    /// first (shared services + in-memory fallback), then call this method
    /// to override the mastery store with the Marten binding. Per memory
    /// "No stubs — production grade" (2026-04-11), the in-memory store is
    /// test-only; production hosts persist BKT posteriors via Marten so
    /// mastery state survives a process restart instead of resetting
    /// every deploy.
    /// </para>
    /// <para>
    /// The other retention stores (<see cref="IExamTargetRetentionExtensionStore"/>,
    /// <see cref="IArchivedExamTargetSource"/>) remain on their in-memory
    /// bindings in this method — their Marten replacements ship in
    /// follow-up commits. Addressing the mastery store first reflects the
    /// "most student-visible" prioritisation: a lost BKT posterior is a
    /// student-facing regression; a lost retention-extension flag is an
    /// ops/compliance-only concern.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSkillKeyedMasteryMarten(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Concrete Marten store registered separately so it can be resolved
        // in isolation (mirrors MartenStudentPlanAggregateStore pattern).
        services.TryAddSingleton<MartenSkillKeyedMasteryStore>();

        // Replace the in-memory mastery-store binding with the Marten one.
        // Use Replace (not Add) so resolution is deterministic regardless
        // of registration order.
        services.Replace(ServiceDescriptor.Singleton<ISkillKeyedMasteryStore>(sp
            => sp.GetRequiredService<MartenSkillKeyedMasteryStore>()));

        // Register the mastery document schema on Marten so the projection
        // table materialises on Host startup.
        services.ConfigureMarten(opts => opts.RegisterMasteryContext());

        return services;
    }

    /// <summary>
    /// Replace the in-memory <see cref="IExamTargetRetentionExtensionStore"/>
    /// binding registered by <see cref="AddExamTargetRetentionServices"/>
    /// with the Marten-backed <see cref="MartenExamTargetRetentionExtensionStore"/>
    /// and register the <see cref="ExamTargetRetentionExtensionDocument"/>
    /// schema via <see cref="ExamTargetRetentionMartenRegistration.RegisterExamTargetRetentionContext"/>.
    /// <para>
    /// Per memory "No stubs — production grade" (2026-04-11), the in-memory
    /// extension store is test-only; production hosts persist the 60-month
    /// opt-in flag (ADR-0050 §6) via Marten so a student who opted in
    /// before a deploy doesn't silently fall back to the 24-month default.
    /// </para>
    /// </summary>
    public static IServiceCollection AddExamTargetRetentionExtensionMarten(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MartenExamTargetRetentionExtensionStore>();
        services.Replace(ServiceDescriptor.Singleton<IExamTargetRetentionExtensionStore>(sp
            => sp.GetRequiredService<MartenExamTargetRetentionExtensionStore>()));
        services.ConfigureMarten(opts => opts.RegisterExamTargetRetentionContext());
        return services;
    }

    /// <summary>
    /// Replace the in-memory <see cref="IArchivedExamTargetSource"/>
    /// binding registered by <see cref="AddExamTargetRetentionServices"/>
    /// with the Marten-backed <see cref="MartenArchivedExamTargetSource"/>
    /// and register the <see cref="ExamTargetShredLedgerDocument"/>
    /// schema via <see cref="ArchivedExamTargetMartenRegistration.RegisterArchivedExamTargetContext"/>.
    /// <para>
    /// Per memory "No stubs — production grade" (2026-04-11), the in-memory
    /// source is test-only; production hosts must read archived-target
    /// state from the canonical StudentPlan event log. The in-memory
    /// source is a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
    /// that no production code ever populates, so shipping it to prod
    /// silently turns the ADR-0050 §6 24-month retention window into a
    /// no-op — this method closes that gap.
    /// </para>
    /// <para>
    /// Composition order: call <see cref="AddExamTargetRetentionServices"/>
    /// first (shared services + in-memory fallback), then this method to
    /// replace the source binding. Requires the StudentPlan aggregate
    /// store (<c>AddStudentPlanServices</c> + <c>AddStudentPlanMarten</c>)
    /// to have been registered upstream — the Marten source composes
    /// against <see cref="IStudentPlanAggregateStore"/> to resolve the
    /// <see cref="Cena.Actors.ExamTargets.ExamTargetCode"/> per archived
    /// target.
    /// </para>
    /// </summary>
    public static IServiceCollection AddArchivedExamTargetSourceMarten(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MartenArchivedExamTargetSource>();
        services.Replace(ServiceDescriptor.Singleton<IArchivedExamTargetSource>(sp
            => sp.GetRequiredService<MartenArchivedExamTargetSource>()));
        services.ConfigureMarten(opts => opts.RegisterArchivedExamTargetContext());
        return services;
    }
}
