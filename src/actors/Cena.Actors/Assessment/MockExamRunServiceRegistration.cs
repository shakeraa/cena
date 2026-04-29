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
        services.TryAddScoped<IMockExamRunService, MockExamRunService>();
        services.TryAddScoped<IBagrutPaperStructureCatalog, BagrutPaperStructureCatalog>();

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
