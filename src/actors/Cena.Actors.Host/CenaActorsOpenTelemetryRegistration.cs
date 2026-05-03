// =============================================================================
// Cena Platform — Actor-host OpenTelemetry registration (PRR-304)
//
// Extracted from Program.cs as part of the PRR-304 LOC drain. Bundles the
// OTLP tracing + metrics pipeline into a single AddCenaActorsOpenTelemetry
// extension method so the host's composition root stays under the 500-LOC
// ratchet (per ADR-0012).
//
// Behaviour-preserving extract: the source set, meter list, exporter
// endpoint, runtime/process/AspNetCore instrumentation, and Prometheus
// exporter on the metrics pipeline are identical to the pre-extract
// inline registration. No telemetry is added or removed.
//
// Source-of-truth: when adding a new ActivitySource or Meter to the
// actor host, add it here so every actor-host instance picks it up.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Cena.Actors.Host;

/// <summary>
/// DI extension that wires the Cena actor host's OpenTelemetry tracing +
/// metrics pipelines (OTLP exporter to <paramref name="otlpEndpoint"/>;
/// Prometheus exporter on the metrics path).
/// </summary>
public static class CenaActorsOpenTelemetryRegistration
{
    /// <summary>
    /// Register the actor-host telemetry pipelines. Idempotent — safe to
    /// call once during host startup composition. Aligns serviceName +
    /// serviceVersion + serviceInstanceId on the resource so traces +
    /// metrics share a stable release identity (matches the SHA tagged
    /// onto Sentry events for cross-system release correlation).
    /// </summary>
    public static IServiceCollection AddCenaActorsOpenTelemetry(
        this IServiceCollection services,
        string otlpEndpoint,
        string serviceName,
        string serviceVersion)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: System.Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddSource("Cena.Actors.StudentActor")
                .AddSource("Cena.Actors.LearningSessionActor")
                .AddSource("Cena.Actors.StagnationDetectorActor")
                .AddSource("Cena.Actors.OutreachSchedulerActor")
                .AddSource("Proto.Actor")
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new System.Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddMeter("Cena.Actors.StudentActor")
                .AddMeter("Cena.Actors.LearningSessionActor")
                .AddMeter("Cena.Actors.LlmCircuitBreaker")
                .AddMeter("Cena.Actors.CurriculumGraph")
                .AddMeter("Cena.Actors.DeadLetterWatcher")
                .AddMeter("Cena.Infrastructure.NatsOutbox")
                .AddMeter("Cena.Actors.Decay")
                .AddMeter("Cena.Actors.Focus")
                .AddMeter("Cena.Actors.HealthAggregator")
                .AddMeter("Cena.Session.Nats")
                .AddMeter("Npgsql")
                .AddMeter("Cena.HttpCircuitBreaker")
                // RDY-OCR-OBSERVABILITY (Phase 4): OCR cascade metrics
                .AddMeter(Cena.Infrastructure.Ocr.Observability.OcrMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new System.Uri(otlpEndpoint))
                .AddPrometheusExporter());

        return services;
    }
}
