// =============================================================================
// Cena Platform — DI registration for the mock-exam (Bagrut שאלון playbook)
// runner. Hooks the bounded-context's Marten registration into Marten's
// store-options builder, registers IMockExamRunService + the structure
// catalog, and (when enabled) the dev-data seeder + retention worker.
// =============================================================================

using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cena.Actors.Assessment;

public static class MockExamRunServiceRegistration
{
    /// <summary>
    /// Activates the mock-exam runner end-to-end:
    ///   - Marten doc + event registration for the bounded context
    ///   - IMockExamRunService scoped lifetime
    ///   - IBagrutPaperStructureCatalog scoped lifetime
    ///   - MockExamDevDataSeeder hosted service (dev only by default)
    ///   - MockExamRetentionWorker hosted service (180-day cleanup)
    /// Idempotent — safe to call from multiple host registrations.
    /// </summary>
    public static IServiceCollection AddMockExamRunner(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.ConfigureMarten(opts => opts.RegisterMockExamRunContext());

        // PRR-322 — cost rate configuration. Bound via the standard
        // IOptions<T> pattern from "Cena:MockExamCostRates" so per-env
        // appsettings can override defaults. Defaults are 2026-Q2
        // estimates; reconcile against vendor invoices monthly.
        if (configuration is not null)
        {
            services.Configure<MockExamCostRateConfig>(
                configuration.GetSection("Cena:MockExamCostRates"));
        }
        else
        {
            // Test/embedded scenarios that don't pass a configuration
            // root still need IOptions<MockExamCostRateConfig> resolvable
            // — register an empty Configure so defaults kick in.
            services.Configure<MockExamCostRateConfig>(_ => { });
        }

        services.TryAddScoped<IMockExamRunService, MockExamRunService>();
        services.TryAddScoped<IBagrutPaperStructureCatalog, BagrutPaperStructureCatalog>();
        // ItemDeliveryGate isn't always registered globally — register it
        // here so MockExamRunService can resolve. TryAddSingleton so a host
        // that already registered a custom impl wins.
        services.TryAddSingleton<IItemDeliveryGate, ItemDeliveryGate>();

        // Dev seeder only registered if the host opts in (configuration null
        // = registered, lets the worker decide via env). Production hosts
        // that want to skip can set `Cena:ExamPrep:DevSeed:Enabled=false`.
        services.AddHostedService<MockExamDevDataSeeder>();

        // Retention worker — only added when host explicitly enables it
        // (caller-side `services.AddHostedService<MockExamRetentionWorker>`
        // is also legal). Default ON: 180-day retention is the floor per
        // ADR-0059 §15.7 analogous mandate.
        services.AddHostedService<MockExamRetentionWorker>();

        return services;
    }
}
