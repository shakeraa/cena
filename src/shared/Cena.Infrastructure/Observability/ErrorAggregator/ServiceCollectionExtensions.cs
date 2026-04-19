// =============================================================================
// Cena Platform — Error Aggregator DI registration (RDY-064)
//
// Called from every .NET host (admin-api, student-api, actor-host, emulator).
// Wires IExceptionScrubber + IErrorAggregator. The concrete aggregator is
// selected by config; today only "null" is supported (graceful-disabled
// scaffold). "sentry" / "appinsights" branches wait on the RDY-064 ADR.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Observability.ErrorAggregator;

public static class ErrorAggregatorServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="IErrorAggregator"/> + <see cref="IExceptionScrubber"/>
    /// based on the <c>ErrorAggregator</c> configuration section. Always
    /// safe to call — on misconfiguration falls back to a null aggregator
    /// rather than throwing, so a bad DSN never breaks startup.
    /// </summary>
    public static IServiceCollection AddCenaErrorAggregator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(ErrorAggregatorOptions.SectionName)
            .Get<ErrorAggregatorOptions>() ?? new ErrorAggregatorOptions();

        services.Configure<ErrorAggregatorOptions>(
            configuration.GetSection(ErrorAggregatorOptions.SectionName));

        // Scrubber is always registered — Serilog enrichers call it even
        // when the aggregator itself is null.
        services.AddSingleton<IExceptionScrubber, ExceptionScrubber>();

        // Backend selection. ADR-gated: until RDY-064 ADR lands, anything
        // other than "null" logs a warning at registration and falls back.
        var backend = (options.Enabled ? options.Backend : "null")?.ToLowerInvariant() ?? "null";
        var dsnConfigured = !string.IsNullOrWhiteSpace(options.Dsn);

        switch (backend)
        {
            case "null":
                services.AddSingleton<IErrorAggregator, NullErrorAggregator>();
                break;

            case "sentry":
            case "appinsights":
                // Concrete impl blocked on RDY-064 ADR. Register Null and
                // log a warning so ops notices the config is ahead of code.
                services.AddSingleton<IErrorAggregator>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<NullErrorAggregator>>();
                    var hostLogger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ErrorAggregator.Setup");
                    hostLogger.LogWarning(
                        "ErrorAggregator:Backend={Backend} is configured but the "
                        + "concrete implementation is gated on RDY-064 ADR. Falling "
                        + "back to NullErrorAggregator. DsnConfigured={DsnConfigured}",
                        backend,
                        dsnConfigured);
                    return new NullErrorAggregator(logger);
                });
                break;

            default:
                services.AddSingleton<IErrorAggregator>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<NullErrorAggregator>>();
                    var hostLogger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ErrorAggregator.Setup");
                    hostLogger.LogWarning(
                        "ErrorAggregator:Backend={Backend} is not recognised. "
                        + "Falling back to NullErrorAggregator. Expected one of: "
                        + "null, sentry, appinsights.",
                        backend);
                    return new NullErrorAggregator(logger);
                });
                break;
        }

        return services;
    }
}
