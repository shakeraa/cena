// =============================================================================
// Cena Platform — DI extension for IRubricVersionPinning (prr-033, ADR-0052)
//
// Reads contracts/rubric/*.yml on boot, registered as a singleton shared
// across tenants (Ministry rubric is global per ADR-0052).
// Dir resolution: `Cena:Rubric:Dir` config key overrides; default falls
// back to the repo-relative path used by the other catalog services.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment.Rubric;

public static class RubricServiceRegistration
{
    public static IServiceCollection AddCenaRubricVersionPinning(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IRubricVersionPinning>(sp =>
        {
            var configured = configuration["Cena:Rubric:Dir"];
            var dir = !string.IsNullOrWhiteSpace(configured)
                ? configured!
                : Path.Combine(
                    environment.ContentRootPath, "..", "..", "..", "contracts", "rubric");
            return new RubricVersionPinningService(
                rubricDir: Path.GetFullPath(dir),
                logger: sp.GetRequiredService<ILogger<RubricVersionPinningService>>());
        });
        return services;
    }
}
