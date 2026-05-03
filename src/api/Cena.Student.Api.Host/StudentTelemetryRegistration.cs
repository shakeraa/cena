// =============================================================================
// Cena Platform — student-host OpenTelemetry / Prometheus DI registration.
//
// Extracted from Program.cs so the 500-LOC ratchet (ADR-0012, baseline 707)
// stays satisfied. Mirrors the existing per-domain registration pattern
// already used for Cas / Auth / Catalog.
//
// Usage:
//   builder.Services.AddCenaStudentTelemetry(builder.Configuration);
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Cena.Student.Api.Host;

internal static class StudentTelemetryRegistration
{
    /// <summary>
    /// Wires the same OTel + Prometheus stack the student host has carried
    /// since RDY-064 / ADR-0058 §3 — service.version flows from
    /// ErrorAggregator:Release → Cluster:ServiceVersion so Sentry release
    /// tags and trace exports stay correlated.
    /// </summary>
    public static IServiceCollection AddCenaStudentTelemetry(
        this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration.GetValue<string>("Cluster:OtlpEndpoint")
            ?? "http://localhost:4317";

        // RDY-064 / ADR-0058 §3: release correlation. service.version shares the
        // CENA_GIT_SHA string that Sentry uses for release tags.
        var otelServiceVersion = configuration["ErrorAggregator:Release"]
            ?? configuration["Cluster:ServiceVersion"]
            ?? "unknown";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "cena-student-api",
                    serviceVersion: otelServiceVersion,
                    serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                // RDY-OCR-OBSERVABILITY (Phase 4): OCR cascade metrics
                .AddMeter(Cena.Infrastructure.Ocr.Observability.OcrMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
                .AddPrometheusExporter());

        return services;
    }
}
