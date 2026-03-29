# REV-018: Fix Observability Stack (Prometheus, OTLP, Admin API Metrics, Port Mismatch)

**Priority:** P2 -- MEDIUM (Prometheus scrapes wrong port, traces go to black hole, Admin API has zero metrics)
**Blocked by:** None
**Blocks:** Production monitoring
**Estimated effort:** 1 day
**Source:** System Review 2026-03-28 -- DevOps Engineer (Findings in sections 7, 3), Cyber Officer 2 (F-LOG-03/04)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The observability stack has good bones (OpenTelemetry + Prometheus + Grafana) but is misconfigured:

1. **Prometheus scrapes `host.docker.internal:5000`** -- Actor Host runs on 5119 (or 5050 per appsettings)
2. **OTLP exporter targets `localhost:4317`** -- no Jaeger/Tempo/OTEL Collector exists in any compose file
3. **Admin API has zero observability** -- no OpenTelemetry, no Prometheus, no custom meters
4. **`start.sh` hardcodes port 5000** -- health checks and process management target wrong port
5. **Grafana uses default password** `cena`
6. **No log aggregation** -- both services log to console only

## Subtasks

### REV-018.1: Fix Prometheus Scrape Target

**File to modify:** `config/prometheus.yml`

```yaml
# BEFORE
- targets: ['host.docker.internal:5000']

# AFTER
- targets: ['host.docker.internal:5119']
  labels:
    service: 'actor-host'
```

**Acceptance:**
- [ ] `curl http://localhost:5119/metrics` returns Prometheus metrics
- [ ] Prometheus targets page shows actor-host as UP
- [ ] Grafana dashboard shows real data (not "No Data")

### REV-018.2: Add OTLP Collector to Observability Stack

**File to modify:** `config/docker-compose.observability.yml`

```yaml
otel-collector:
  image: otel/opentelemetry-collector-contrib:0.96.0
  command: ["--config=/etc/otel-collector.yaml"]
  volumes:
    - ./otel-collector.yaml:/etc/otel-collector.yaml:ro
  ports:
    - "4317:4317"   # OTLP gRPC
    - "4318:4318"   # OTLP HTTP

jaeger:
  image: jaegertracing/all-in-one:1.54
  ports:
    - "16686:16686"  # Jaeger UI
  environment:
    COLLECTOR_OTLP_ENABLED: true
```

**File to create:** `config/otel-collector.yaml`

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

exporters:
  otlp/jaeger:
    endpoint: jaeger:4317
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlp/jaeger]
```

**Acceptance:**
- [ ] OTLP collector receives traces from Actor Host
- [ ] Jaeger UI at `http://localhost:16686` shows traces
- [ ] Traces include actor activations, LLM calls, NATS routing

### REV-018.3: Add OpenTelemetry to Admin API

**File to modify:** `src/api/Cena.Api.Host/Program.cs`

Add the same OpenTelemetry configuration that the Actor Host has:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("cena-admin-api"))
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddPrometheusExporter());

// Add Prometheus scrape endpoint
app.MapPrometheusScrapingEndpoint();
```

**Update Prometheus config** to also scrape the Admin API:
```yaml
- targets: ['host.docker.internal:5050']
  labels:
    service: 'admin-api'
```

**Acceptance:**
- [ ] Admin API exports Prometheus metrics at `/metrics`
- [ ] Admin API sends traces to OTLP collector
- [ ] Prometheus scrapes both Actor Host and Admin API

### REV-018.4: Fix start.sh Port References

**File to modify:** `scripts/start.sh`

Replace all references to port 5000 with the correct ports:
- Actor Host: 5119 (or read from appsettings)
- Admin API: 5050

**Acceptance:**
- [ ] `./scripts/start.sh` starts services and health checks pass
- [ ] `--stop` kills the correct processes
- [ ] No hardcoded port 5000 references remain in start.sh

### REV-018.5: Add Grafana Password to Environment Variable

**File to modify:** `config/docker-compose.observability.yml`

```yaml
# BEFORE
GF_SECURITY_ADMIN_PASSWORD: cena

# AFTER
GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_ADMIN_PASSWORD:-cena_dev_grafana}
```

**Acceptance:**
- [ ] Grafana password is configurable via environment variable
- [ ] Default dev password is not `cena` (too short/obvious)
