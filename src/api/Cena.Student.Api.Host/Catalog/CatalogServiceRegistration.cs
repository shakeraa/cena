// =============================================================================
// Cena Platform — DI extension for the exam-catalog service (prr-220)
//
// Pulled out of Program.cs so the composition-root baseline doesn't grow;
// mirrors the `AddCenaSignalR` / `AddCenaRedisSessionStoreMetrics` pattern
// used elsewhere in the student host.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Catalog;

public static class CatalogServiceRegistration
{
    /// <summary>
    /// Register the exam-catalog service + its tenant-overlay store. Reads
    /// the YAML under `contracts/exam-catalog/` via `Cena:ExamCatalog:Dir`
    /// or the default relative path from the host's content root.
    /// </summary>
    public static IServiceCollection AddCenaExamCatalog(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<ITenantCatalogOverlayStore, NullTenantCatalogOverlayStore>();
        services.AddSingleton<IExamCatalogService>(sp =>
        {
            var configured = configuration["Cena:ExamCatalog:Dir"];
            var dir = !string.IsNullOrWhiteSpace(configured)
                ? configured!
                : Path.Combine(environment.ContentRootPath, "..", "..", "..", "contracts", "exam-catalog");
            return new ExamCatalogService(
                catalogDir: Path.GetFullPath(dir),
                overlayStore: sp.GetRequiredService<ITenantCatalogOverlayStore>(),
                logger: sp.GetRequiredService<ILogger<ExamCatalogService>>());
        });
        return services;
    }
}
