// =============================================================================
// Cena Platform — PhotoDiagnosticServiceRegistration (EPIC-PRR-J PRR-380..423/391)
//
// DI wiring for the entire PhotoDiagnostic bounded context. Two composition
// modes mirror the SubscriptionServiceRegistration (EPIC-PRR-I) pattern:
//   AddPhotoDiagnostic(services)       - InMemory stores (dev/test)
//   AddPhotoDiagnosticMarten(services) - Marten-backed stores (prod)
//
// Both register the shared services (metrics, tracker, scorer, assembler,
// quota gate, dispute service, escalation policy, credit service). Callers
// must have AddSubscriptions/AddSubscriptionsMarten already registered so
// the entitlement resolver + cap enforcer are available.
//
// The retention worker is added as a hosted service only in Marten mode
// (it's pointless against a process-local in-memory store).
//
// PRR-391: registers IDiagnosticCreditLedger + IDiagnosticCreditDispatcher
// (Null fallback) + IDiagnosticCreditService. The Null dispatcher is a
// legitimate fallback (not a stub) — see NullDiagnosticCreditDispatcher.cs.
// Hosts that want real email/SMS apology delivery replace the binding
// before calling AddPhotoDiagnostic/AddPhotoDiagnosticMarten, and TryAdd
// will leave the override alone.
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
        // PRR-391: support-issued credit ledger (in-memory for dev/test).
        services.TryAddSingleton<IDiagnosticCreditLedger, InMemoryDiagnosticCreditLedger>();
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
        // PRR-391: Marten-backed credit ledger in production.
        services.TryAddSingleton<IDiagnosticCreditLedger, MartenDiagnosticCreditLedger>();
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

        // PRR-391: one-click support-agent credit flow. The Null dispatcher
        // is a legitimate fallback (not a stub) — hosts that wire a real
        // IEmailSender-backed dispatcher register it before this call and
        // TryAdd preserves the override.
        services.TryAddSingleton<IDiagnosticCreditDispatcher, NullDiagnosticCreditDispatcher>();
        services.TryAddSingleton<IDiagnosticCreditService, DiagnosticCreditService>();
    }
}
