// =============================================================================
// Cena Platform — Error Aggregator DI registration (RDY-064 / ADR-0058)
//
// Called from every .NET host (admin-api, student-api, actor-host, emulator).
// Wires IExceptionScrubber + IErrorAggregator. ADR-0058 unblocked the
// "sentry" branch — it now resolves the real SentryErrorAggregator when a
// non-empty DSN is configured, and gracefully degrades to Null when the DSN
// is empty (e.g. local dev without a project). "appinsights" remains gated
// (future work) and still falls back to Null + warning.
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

        // Backend selection. ADR-0058 unblocks "sentry"; "appinsights" remains
        // gated (future work).
        var backend = (options.Enabled ? options.Backend : "null")?.ToLowerInvariant() ?? "null";
        var dsnConfigured = !string.IsNullOrWhiteSpace(options.Dsn);

        switch (backend)
        {
            case "null":
                services.AddSingleton<IErrorAggregator, NullErrorAggregator>();
                break;

            case "sentry":
                // ADR-0058 §1 — Sentry SaaS (EU region). Real backend when a
                // DSN is present. Empty DSN → graceful Null fallback (same
                // posture as no-credentials peripherals like SMTP).
                if (!dsnConfigured)
                {
                    services.AddSingleton<IErrorAggregator>(sp =>
                    {
                        var nullLogger = sp.GetRequiredService<ILogger<NullErrorAggregator>>();
                        var setupLogger = sp.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("ErrorAggregator.Setup");
                        setupLogger.LogInformation(
                            "ErrorAggregator:Backend=sentry configured but DSN is empty. "
                            + "Registering NullErrorAggregator (graceful-disabled).");
                        return new NullErrorAggregator(nullLogger);
                    });
                }
                else
                {
                    services.AddSingleton<IErrorAggregator, SentryErrorAggregator>();
                }
                break;

            case "appinsights":
                // Concrete impl still gated — ADR-0058 only covered Sentry.
                services.AddSingleton<IErrorAggregator>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<NullErrorAggregator>>();
                    var hostLogger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ErrorAggregator.Setup");
                    hostLogger.LogWarning(
                        "ErrorAggregator:Backend=appinsights is configured but the "
                        + "concrete implementation is not yet wired (ADR-0058 covered "
                        + "Sentry only). Falling back to NullErrorAggregator. "
                        + "DsnConfigured={DsnConfigured}",
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
