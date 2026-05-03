# RES-002: Export Metrics to Prometheus/Grafana

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P0 -- Immediate                              |
| **Effort**    | Medium (4-6 hours)                           |
| **Impact**    | High -- Epic's #1 lesson: "invest in logging early" |
| **Origin**    | Epic Games post-incident: "Metrics collection is a conversation that happens with a system over time" |
| **Status**    | DONE                                         |
| **Execution** | See [EXECUTION.md](EXECUTION.md#res-002-observability-stack--p0) |

---

## Problem

Cena has `System.Diagnostics.Metrics` counters and histograms defined across actors, but no export pipeline. Metrics exist in-process and disappear when the process stops. Without dashboards and alerts, problems are invisible until catastrophic -- exactly Fortnite's failure mode.

## Existing Metrics (Already Defined)

### StudentActor
- `cena.student.attempts_total` (counter)
- `cena.student.event_persist_ms` (histogram)
- `cena.student.memory_bytes` (histogram)
- `cena.student.activations_total` (counter)

### LlmCircuitBreakerActor
- `cena.llm.circuit_opened_total` (counter)
- `cena.llm.requests_rejected_total` (counter)

### StudentActorManager
- Activation rate, pool size, queue depth (likely instrumented)

## Implementation

### 1. Add OpenTelemetry Metrics Exporter

```bash
dotnet add src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj package OpenTelemetry.Exporter.Prometheus.AspNetCore
dotnet add src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj package OpenTelemetry.Extensions.Hosting
```

### 2. Configure in Program.cs

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Cena.Actors.StudentActor");
        metrics.AddMeter("Cena.Actors.LlmCircuitBreaker");
        metrics.AddMeter("Cena.Actors.StudentActorManager");
        metrics.AddPrometheusExporter();
    });

// Expose /metrics endpoint
app.MapPrometheusScrapingEndpoint();
```

### 3. Docker Compose for Prometheus + Grafana

```yaml
# config/docker-compose.observability.yml
services:
  prometheus:
    image: prom/prometheus:latest
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=cena
```

### 4. Grafana Dashboard Panels

| Panel                        | Metric                                | Alert Threshold       |
|------------------------------|---------------------------------------|-----------------------|
| Active Actors                | `cena.student.activations_total`      | >8,000 (80% of cap)  |
| Event Persist Latency (p99)  | `cena.student.event_persist_ms`       | >100ms                |
| Circuit Breaker Opens        | `cena.llm.circuit_opened_total`       | Any increment         |
| Requests Rejected            | `cena.llm.requests_rejected_total`    | >10/min               |
| Memory per Actor             | `cena.student.memory_bytes`           | >400KB avg            |
| Concept Attempts/sec         | `cena.student.attempts_total` rate    | Trend monitoring      |

## Acceptance Criteria

- [ ] `/metrics` endpoint exposes all Cena metrics in Prometheus format
- [ ] Prometheus scrapes successfully at 15s interval
- [ ] Grafana dashboard with all 6 panels above
- [ ] Alert rules configured for thresholds
- [ ] `docker compose up` brings up the full observability stack
- [ ] README in `config/` explains how to use it

## Why This Matters

Epic's post-outage retrospective put observability as lesson #1. They said they should have invested in comprehensive metrics *before* hitting scale problems. Cena already defines the metrics -- just need the pipeline.
