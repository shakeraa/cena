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
}
