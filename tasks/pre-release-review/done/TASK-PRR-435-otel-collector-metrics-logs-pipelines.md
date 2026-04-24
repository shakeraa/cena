# TASK-PRR-435: OTel Collector pipelines for metrics + logs (adds Loki to stack)

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona-sre, persona-privacy (log PII handling)
**Source docs**: [config/otel-collector.yaml](../../config/otel-collector.yaml) — current pipeline is traces-only
**Assignee hint**: kimi-coder + privacy review for log pipeline
**Tags**: source=observability-audit-2026-04-22, epic=epic-prr-l, priority=p0, otel, logs, metrics, sre
**Status**: Ready (can run in parallel with PRR-433)
**Tier**: launch
**Epic**: [EPIC-PRR-L](EPIC-PRR-L-observability-completion.md)

---

## Why

`config/otel-collector.yaml` has exactly one pipeline:

```yaml
traces:
  receivers: [otlp]
  exporters: [otlp/jaeger]
```

Metrics emitted via OTLP (which is how the .NET SDK exports application-layer metrics like `cena_llm_cost_usd_total` or `cena_cas_verifications_total`) arrive at the collector and are **dropped**. Logs are not received at all.

Serilog in all three hosts writes structured logs to the console. If a container restarts, those logs are gone. If a session flow crosses three services (student-api → actor-host → admin-api), there's no way to correlate the entries — they land in three unrelated Docker log streams. An incident investigation today requires `docker logs | grep trace_id` across three containers by hand. That doesn't scale past one engineer and one incident.

The root cause is that the OTel Collector was deployed with a minimum-viable traces pipeline and never extended. This task fills in metrics + logs to complete the "three pillars" of observability with actual working pipelines.

Per memory "Senior Architect mindset" — observability is a system; two out of three pillars working is one pillar short of useful during an incident.

## How

### Metrics pipeline

Extend `config/otel-collector.yaml`:

```yaml
exporters:
  prometheusremotewrite:
    endpoint: http://prometheus:9090/api/v1/write
    tls:
      insecure: true

service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [batch, resource]
      exporters: [prometheusremotewrite]
```

Prometheus needs the `--web.enable-remote-write-receiver` flag (add to the Prometheus service command in `docker-compose.observability.yml`). OTLP-exported metrics now land in the same Prometheus instance as scrape-exported metrics, so `rate(cena_llm_cost_usd_total[5m])` works regardless of whether the metric came from `/metrics` scraping or OTLP pushing.

**Alternatives rejected**:
- Prometheus OTLP receiver (native, no collector hop): simpler but puts more work on the Prometheus process. Losing the collector's processor stage (batch, resource enrichment, PII-scrub hook point) is not worth the one-hop saving.
- Pushgateway: wrong model (push vs pull); also loses the missing-heartbeat signal.

### Logs pipeline

Add Loki to `config/docker-compose.observability.yml`:

- Image: `grafana/loki:2.9`.
- Config mounted from `config/loki-config.yaml`.
- Single-binary mode (monolithic) at Launch scale; revisit microservice-mode at Growth (T4) scale.
- Persistent volume for log chunks + index.
- Retention: 30 days to match [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) session-scope boundary for any session-derived log lines. (Note: logs in Cena can contain misconception references — see `SessionRiskLogEnricher`; 30d respects that.)

Extend OTel Collector:

```yaml
exporters:
  loki:
    endpoint: http://loki:3100/loki/api/v1/push

service:
  pipelines:
    logs:
      receivers: [otlp]
      processors: [batch, resource, attributes/redact]
      exporters: [loki]
```

The `attributes/redact` processor drops any attribute whose key matches a denylist (`password`, `token`, `api_key`, `authorization`, `firebase_id_token`). Belt-and-suspenders with the existing [PiiDestructuringPolicy + SessionRiskLogEnricher](../../src/actors/Cena.Actors.Host/Program.cs#L84-L91). The persona-privacy sign-off requires both layers.

### Serilog OTLP sink

Each of the three host Program.cs entries gets the Serilog OTLP sink added:

```csharp
.WriteTo.OpenTelemetry(options =>
{
    options.Endpoint = otlpEndpoint; // same endpoint as tracing
    options.Protocol = OtlpProtocol.Grpc;
    options.ResourceAttributes = new Dictionary<string, object>
    {
        ["service.name"] = ServiceName,
        ["service.instance.id"] = Environment.MachineName,
    };
})
```

`trace_id` + `span_id` are auto-attached by Serilog's OTel integration when an ActivitySource is active. This is what makes trace-log correlation work: a log entry inside an `Activity.StartActivity("CasVerification")` scope carries the span ID automatically, and Grafana's Loki-to-Tempo link jumps from the log line to the trace.

### Grafana datasource provisioning

Add Loki to `config/grafana/provisioning/datasources/datasources.yml`. Pre-provisioned so `docker-compose up` gives a developer a working Logs panel without clicking anything.

### Validation

- `curl http://otel-collector:13133/health` returns healthy (the collector's zpages health endpoint).
- A log line emitted via Serilog in student-api reaches Loki within 5 seconds (verified by tail + grep).
- A metric emitted via `Meter.CreateCounter` in admin-api shows up in Prometheus within 30 seconds.
- Trace-log correlation: clicking a span in Jaeger → "Logs for this trace" → Loki query with `trace_id` filter returns the related log lines.
- PII scrubber test: emit a log line containing `password=secret123`; confirm the Loki-stored entry does NOT contain that substring.

## Files

- `config/otel-collector.yaml` — extend with metrics + logs pipelines + processors.
- `config/docker-compose.observability.yml` — add Loki service, add `--web.enable-remote-write-receiver` to Prometheus command.
- `config/loki-config.yaml` (new).
- `config/grafana/provisioning/datasources/datasources.yml` — add Loki datasource.
- `src/api/Cena.Student.Api.Host/Program.cs` — add Serilog OTLP sink.
- `src/api/Cena.Admin.Api.Host/Program.cs` — same.
- `src/actors/Cena.Actors.Host/Program.cs` — same.
- `src/actors/Cena.Actors.Tests/Observability/PiiScrubberReachesLokiTest.cs` (new integration test — emits a known-PII line, asserts Loki-stored version is scrubbed).
- `docs/ops/runbooks/observability-operations.md` — extend with "investigate across services via trace_id" section.

## Definition of Done

- OTel Collector `/health` returns healthy in Dev + staging.
- Metrics emitted via OTLP are queryable in Prometheus within 30s (verified by a test metric).
- Logs emitted via Serilog are queryable in Loki within 5s (verified by a test log).
- Grafana has Loki as a pre-provisioned datasource; `Explore → Loki` works without any manual configuration.
- Trace-log correlation verified: click span → jump to correlated log lines in Loki.
- PII scrubber integration test passes: known-PII log line lands in Loki without the PII substring.
- Loki retention is 30d and documented.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- Memory "No stubs — production grade" — real Serilog sink, real Loki, real scrubbing.
- Memory "Senior Architect mindset" — three pillars as a system, not three separate installs.
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — 30d retention for session-derived data includes logs.
- [PRR-022](TASK-PRR-022-ban-pii-in-llm-prompts-lint-rule-adr.md) — PII scrubbing is mandatory before any log leaves the service process.
- Memory "Full sln build gate".
- Memory "Check container state before build" — verify compose is healthy before declaring done.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + Loki query screenshot + PII-scrubber test output>"`

## Related

- EPIC-PRR-L.
- PRR-433 — scrape-based metrics join OTLP-sourced metrics in the same Prometheus.
- PRR-434 — alerts may fire on OTLP-sourced metrics once this pipeline lands.
- PRR-436 — dashboards query both Prometheus + Loki.
