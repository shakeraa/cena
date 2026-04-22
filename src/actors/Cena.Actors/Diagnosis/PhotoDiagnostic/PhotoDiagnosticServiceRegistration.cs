// =============================================================================
// Cena Platform — PhotoDiagnosticServiceRegistration (EPIC-PRR-J PRR-380..423)
//
// DI wiring for the entire PhotoDiagnostic bounded context. Two composition
// modes mirror the SubscriptionServiceRegistration (EPIC-PRR-I) pattern:
//   AddPhotoDiagnostic(services)       - InMemory stores (dev/test)
//   AddPhotoDiagnosticMarten(services) - Marten-backed stores (prod)
//
// Both register the shared services (metrics, tracker, scorer, assembler,
// quota gate, dispute service, escalation policy). Callers must have
// AddSubscriptions/AddSubscriptionsMarten already registered so the
// entitlement resolver + cap enforcer are available.
//
// The retention worker is added as a hosted service only in Marten mode
// (it's pointless against a process-local in-memory store).
// =============================================================================

using Cena.Actors.Cas;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public static class PhotoDiagnosticServiceRegistration
{
    /// <summary>
    /// Register with InMemory stores. Dev/test default.
    /// Assumes ICasRouterService + IStudentEntitlementResolver + IPerTierCapEnforcer
    /// are already registered by upstream AddCas/AddSubscriptions calls.
    /// </summary>
    public static IServiceCollection AddPhotoDiagnostic(this IServiceCollection services)
    {
        services.TryAddSingleton<IPhotoDiagnosticMonthlyUsage, InMemoryPhotoDiagnosticMonthlyUsage>();
        services.TryAddSingleton<IDiagnosticDisputeRepository, InMemoryDiagnosticDisputeRepository>();
        AddSharedServices(services);
        return services;
    }

    /// <summary>
    /// Register with Marten-backed stores and add the retention worker as
    /// an IHostedService. Requires AddMarten to have been called first.
    /// </summary>
    public static IServiceCollection AddPhotoDiagnosticMarten(this IServiceCollection services)
    {
        services.TryAddSingleton<IPhotoDiagnosticMonthlyUsage, MartenPhotoDiagnosticMonthlyUsage>();
        services.TryAddSingleton<IDiagnosticDisputeRepository, MartenDiagnosticDisputeRepository>();
        services.AddHostedService<DiagnosticDisputeRetentionWorker>();
        AddSharedServices(services);
        return services;
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.TryAddSingleton<PhotoDiagnosticMetrics>();

        services.TryAddSingleton<IStepChainVerifier>(sp =>
            new StepChainVerifier(sp.GetRequiredService<ICasRouterService>()));
        services.TryAddSingleton<ITemplateMatchingScorer, TemplateMatchingScorer>();

        services.TryAddSingleton<IPhotoDiagnosticConfidenceTracker, PhotoDiagnosticConfidenceTracker>();
        services.TryAddSingleton<IPhotoDiagnosticLatencyTimer, PhotoDiagnosticLatencyTimer>();

        services.TryAddSingleton<IAccuracyAuditSampler, AccuracyAuditSampler>();
        services.TryAddSingleton<IPhotoDiagnosticAuditLog, LoggingPhotoDiagnosticAuditLog>();

        services.TryAddSingleton<IDiagnosticOutcomeAssembler, DiagnosticOutcomeAssembler>();

        services.TryAddSingleton<IPhotoDiagnosticQuotaGate, PhotoDiagnosticQuotaGate>();

        services.TryAddSingleton<IDiagnosticDisputeService, DiagnosticDisputeService>();
        services.TryAddSingleton<IPhotoDiagnosticGdprService, PhotoDiagnosticGdprService>();
        services.TryAddSingleton<ISupportEscalationPolicy, SupportEscalationPolicy>();
    }
}
