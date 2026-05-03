// =============================================================================
// Cena Platform — DI extension for ISubProcessorRegistry (prr-035)
//
// Reads contracts/privacy/sub-processors.yml on boot, registered as a
// singleton. Path resolution follows the same convention as the exam
// catalog and rubric loaders.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cena.Actors.Infrastructure.Privacy;

public static class SubProcessorRegistryRegistration
{
    public static IServiceCollection AddCenaSubProcessorRegistry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<ISubProcessorRegistry>(sp =>
        {
            var configured = configuration["Cena:Privacy:SubProcessorsPath"];
            var path = !string.IsNullOrWhiteSpace(configured)
                ? configured!
                : Path.Combine(
                    environment.ContentRootPath, "..", "..", "..", "contracts",
                    "privacy", "sub-processors.yml");
            return new SubProcessorRegistry(Path.GetFullPath(path));
        });
        return services;
    }
}
