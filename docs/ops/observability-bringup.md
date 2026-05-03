# Observability Stack — Local Bring-up

> Ops runbook for running Prometheus, Grafana, Jaeger, Loki, OTel Collector and
> Alertmanager against the local Cena dev stack. The admin dashboard's
> Observability card links to these URLs verbatim.

## TL;DR

```bash
# From repo root, with the main app stack already up (postgres, nats, *-api, actor-host)
docker compose --project-name cena \
  -f docker-compose.yml \
  -f docker-compose.app.yml \
  -f config/docker-compose.observability.yml \
  up -d prometheus alertmanager grafana otel-collector loki jaeger

# Recreate the .NET services so they pick up Cluster__OtlpEndpoint
docker compose --project-name cena \
  -f docker-compose.yml \
  -f docker-compose.app.yml \
  -f config/docker-compose.observability.yml \
  up -d --no-build --no-deps admin-api student-api actor-host
```

Then drive load:

```bash
EMU_STUDENTS=20 docker compose --project-name cena \
  -f docker-compose.yml -f docker-compose.app.yml \
  -f config/docker-compose.observability.yml \
  --profile emulator up -d --no-build emulator
```

## Dashboards (the URLs the admin card links to)

| Tool          | URL                          | Auth                              | What's in it |
|---------------|------------------------------|-----------------------------------|--------------|
| Grafana       | http://localhost:3000        | `admin` / `cena_dev_grafana`*     | Pre-provisioned dashboards: Cena Actors, API Golden Signals, LLM Cost, NATS Queue, Photo Pipeline, OCR Cascade. Datasources: Prometheus, Loki, Jaeger. |
| Prometheus    | http://localhost:9090        | none                              | Targets page → http://localhost:9090/targets ; Graph page for ad-hoc PromQL. |
| Jaeger UI     | http://localhost:16686       | none                              | Service dropdown → `cena-learner-service` (actor-host), `cena-student-api`, `cena-admin-api`. |
| Alertmanager  | http://localhost:9093        | none                              | Alert routing + silences. PRR-434 critical alerts. |
| Loki API      | http://localhost:3100        | none                              | Query via Grafana → Explore → Loki. **Currently empty — see "Known gaps."** |

\* Override via `GRAFANA_ADMIN_PASSWORD` env var before `docker compose up`.

## What's wired

### Metrics path: services → Prometheus

ASP.NET services expose `/metrics` (Prometheus exposition format) on the same
Kestrel port that serves HTTP traffic, courtesy of
`OpenTelemetry.AddPrometheusExporter()` in each `Program.cs`. Prometheus scrapes
them every 15 s over the `cena_default` Docker network:

| Job                  | Target                      | Source              |
|----------------------|-----------------------------|---------------------|
| `cena-actor-host`    | `cena-actor-host:5050`      | actor-host Kestrel  |
| `cena-admin-api`     | `cena-admin-api:5052`       | admin-api Kestrel   |
| `cena-student-api`   | `cena-student-api:5050`     | student-api Kestrel |
| `cena-otel-collector`| `otel-collector:8889`       | OTel Collector self |
| `prometheus`         | `localhost:9090`            | Prometheus self     |

Verification:

```bash
curl -s 'http://localhost:9090/api/v1/query?query=up' | jq '.data.result[] | {job:.metric.job, up:.value[1]}'
```

All five jobs should report `"up": "1"`.

### Trace path: services → OTel Collector → Jaeger

.NET services emit OTLP/gRPC to `http://otel-collector:4317`. The collector's
`traces` pipeline (config/otel-collector.yaml) runs PII-redaction +
deployment.environment tagging, then exports to `jaeger:4317`.

Service-to-Jaeger-name mapping (set in each `Program.cs` via
`.AddService(serviceName: ...)`):

| .NET host          | Jaeger service name      | Notable spans                     |
|--------------------|--------------------------|-----------------------------------|
| actor-host         | `cena-learner-service`   | `StudentActor.AttemptConcept`, `LearningSessionActor.*` |
| student-api        | `cena-student-api`       | ASP.NET Core HTTP server spans, HttpClient spans to actor-host |
| admin-api          | `cena-admin-api`         | ASP.NET Core HTTP server spans |

Verification:

```bash
curl -s 'http://localhost:16686/api/services' | jq
# Then drill down on any service:
curl -s 'http://localhost:16686/api/traces?service=cena-learner-service&limit=3&lookback=5m' | jq '.data | length'
```

### Logs path: ⚠️ collector ready, .NET pipeline not wired

The OTel Collector has a `logs` pipeline that exports to Loki, but **no .NET
service currently exports logs over OTLP**. `Program.cs` configures only
`.WithTracing()` and `.WithMetrics()` — there is no `.WithLogging()` call and
no `Logging:OpenTelemetry` provider configured. As a result, Loki is reachable
but receives no application logs.

To close this gap:

```csharp
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
});
```

Tracked as a known gap; not addressed in this bring-up.

## Driving real traffic (the emulator)

The student emulator (`src/emulator/`) connects to NATS, simulates concurrent
students working through learning sessions, and exercises the full
StudentActor / LearningSessionActor lifecycle. With OTel wired, every actor
invocation emits a real Jaeger trace.

```bash
# Tunables (defaults in parens)
EMU_STUDENTS=20      # students-per-day target (50)
EMU_SPEED=5          # time-acceleration factor (5×; 25× is the known-broken
                     # threshold per the seq-collision postmortem)
EMU_DURATION=3600    # seconds before exit (default: until killed)

EMU_STUDENTS=20 docker compose --project-name cena \
  -f docker-compose.yml -f docker-compose.app.yml \
  -f config/docker-compose.observability.yml \
  --profile emulator up -d --no-build emulator

docker logs -f cena-emulator    # watch arrivals + event-replay catch-up
```

A 20-student run produces ~50–100 events/sec into Marten and yields
`StudentActor.AttemptConcept` spans visible in Jaeger within 30 s of starting.

## Known gaps + non-scrapable services

Two services from earlier `prometheus.yml` revisions were removed because they
do not expose Prometheus exposition format:

- **NATS** (`cena-nats:8222`) exposes `/varz` (JSON) only. Add a
  `prometheus-nats-exporter` sidecar if NATS metrics are needed. Until then,
  observe NATS health via the actor-host's NATS-client metrics.
- **SymPy sidecar** (`cena-sympy-sidecar`) is a pure NATS subscriber with no
  HTTP listener. Its health is observed via NATS request/reply latency emitted
  from actor-host.

## Production deployment

The amber notice on the admin dashboard's Observability card ("Links point to
local development. Update for production deployment.") is correct. For prod:

- Replace `localhost:3000`, `:9090`, `:16686` with the cluster's ingress
  hostnames (see `deploy/helm/cena/values.yaml` for the Helm side).
- Move Grafana admin auth off the `${GRAFANA_ADMIN_PASSWORD}` shared secret;
  use Grafana's OIDC/OAuth provider against the same Firebase/IdP the rest of
  the platform uses.
- Drop the `0.0.0.0` listeners (OTel collector warns about this on startup).
  Bind on the cluster-internal interface only.
- The `loki` exporter currently uses `tls.insecure: true` over the compose
  network — fine for local, must be swapped for mTLS in prod.

## Tearing down

```bash
docker compose --project-name cena \
  -f docker-compose.yml -f docker-compose.app.yml \
  -f config/docker-compose.observability.yml \
  --profile emulator down emulator prometheus alertmanager grafana otel-collector loki jaeger
```

The `grafana_data` named volume persists provisioned dashboards across restarts;
remove with `docker volume rm cena_grafana_data` if you need a clean slate.

## Files

- `config/docker-compose.observability.yml` — six-service compose stack
- `config/prometheus.yml` — scrape config (5 jobs)
- `config/prometheus/rules/cena-critical-alerts.yml` — PRR-434 alert rules
- `config/alertmanager.yml` — alert routing
- `config/otel-collector.yaml` — traces/metrics/logs pipelines + PII redaction
- `config/grafana/provisioning/datasources/prometheus.yml` — Prometheus, Loki, Jaeger
- `config/grafana/provisioning/dashboards/` — dashboard provisioning
- `config/grafana/dashboards/*.json` — six pre-built dashboards
- `docker-compose.app.yml` — `Cluster__OtlpEndpoint` env on admin-api, student-api, actor-host
