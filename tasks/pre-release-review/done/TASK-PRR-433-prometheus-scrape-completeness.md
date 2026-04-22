# TASK-PRR-433: Prometheus scrape-target completeness

**Priority**: P0
**Effort**: S (2-3 days)
**Lens consensus**: persona-sre
**Source docs**: [config/prometheus.yml](../../config/prometheus.yml) — current config is incomplete
**Assignee hint**: kimi-coder
**Tags**: source=observability-audit-2026-04-22, epic=epic-prr-l, priority=p0, sre, infra
**Status**: Ready
**Tier**: launch
**Epic**: [EPIC-PRR-L](EPIC-PRR-L-observability-completion.md)

---

## Why

`config/prometheus.yml` declares two scrape targets: `cena-actors-host` (port 5119) and `cena-admin-api` (port 5050). **Student API is absent.** Student API is the hot path — every session, every item fetch, every answer submission goes through it. It exposes a `/metrics` endpoint (standard ASP.NET Core OpenTelemetry wiring) but nothing pulls from it. Every dashboard that reads "API metrics" reads half the system and reports it as the whole.

SymPy sidecar, Redis, and Postgres are also unscraped. SymPy's availability is literally an alert condition (`ALERT-CAS-001`), but its health depends on a metric that does not reach Prometheus. Redis eviction + cache-hit rate drives every finops decision in [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) + [PRR-047](TASK-PRR-047-llm-prompt-cache-enforcement-hit-rate-slo.md) + [PRR-233](TASK-PRR-233-prompt-cache-slo-per-target.md). Postgres is the Marten event store; saturation there is the single biggest scheduler-failure mode under Bagrut load.

The root cause is not that the instrumentation is missing. All four services either already emit or can emit metrics trivially. The root cause is that `prometheus.yml` was authored once and never extended as the service list grew. This task closes the gap.

## How

### Student API scrape target

The Program.cs at `src/api/Cena.Student.Api.Host/Program.cs` registers OpenTelemetry (grep confirmed). It already maps `/metrics` via the default ASP.NET Core OTel exporter. Add a `cena-student-api` scrape job to `prometheus.yml`:

- Target: `host.docker.internal:<student-api-port>` in Dev (the actual port — verify against the docker-compose mapping; PWA config hints at port 5050 for admin-api but student-api is on a different port).
- In k8s (post-[PRR-430](TASK-PRR-430-k3d-actor-cluster-dev-environment.md)), use Prometheus ServiceMonitor discovery instead of static_configs. This task adds the Dev-side static config; the k8s-side ServiceMonitor lands with PRR-430 helm chart work.
- Scrape interval: 15s, matching the existing targets.
- Labels: `service: "student-api"` for consistent querying.

### SymPy sidecar

The SymPy sidecar is Python. Options:
- **Chosen**: add `prometheus_client` to the sidecar (~15 LOC) and expose `/metrics` on the sidecar's existing HTTP port. Wire counter + histogram for verification calls, latency, error rate. Scrape from prometheus.yml.
- **Rejected**: Prometheus PushGateway — introduces a third component + a push model that obscures the missing-heartbeat signal (the opposite of what we want for `ALERT-CAS-001`).
- **Rejected**: custom Node exporter for SymPy processes — doesn't surface application-layer metrics (verification counts, error classes).

### Redis exporter

Add `oliver006/redis_exporter` sidecar to `docker-compose.observability.yml`. Point it at the existing Redis connection (reuse `REDIS_PASSWORD`). Scrape from prometheus.yml. This gives us `redis_memory_used_bytes`, `redis_evicted_keys_total`, `redis_keyspace_hits_total`, `redis_keyspace_misses_total` — the exact metrics the cache-hit-rate SLO (PRR-047, PRR-233) needs.

### Postgres exporter

Add `prometheuscommunity/postgres-exporter` sidecar. Point at the existing Postgres connection (reuse `POSTGRES_PASSWORD`). Scrape from prometheus.yml. Gives us connection-count, transaction-rate, replication-lag, WAL-growth, table-size. Surfaces the Marten-event-store saturation warning that currently has no instrument.

### Validation

For every new target, a developer can run `curl http://prometheus:9090/api/v1/targets` and see the target in `activeTargets` with `health: "up"`. A test script at `scripts/ops/verify-scrape-targets.sh` curls the Prometheus API and asserts all expected targets are up, exits non-zero if any are down.

## Files

- `config/prometheus.yml` — extend `scrape_configs` with 4 new jobs (student-api, sympy-sidecar, redis, postgres).
- `config/docker-compose.observability.yml` — add `redis_exporter` + `postgres_exporter` services.
- `src/sympy-sidecar/app.py` (or wherever the sidecar lives) — add `prometheus_client` instrumentation, expose `/metrics` on existing HTTP port.
- `scripts/ops/verify-scrape-targets.sh` — new; validates all targets are healthy.
- `docs/ops/runbooks/observability-operations.md` — new or extend existing; section "adding a scrape target."

## Definition of Done

- `curl http://prometheus:9090/api/v1/targets | jq '.data.activeTargets | length'` returns ≥ 6.
- `scripts/ops/verify-scrape-targets.sh` exits 0 locally + in CI.
- For each new target, a dashboard query returning a non-zero time-series value has been observed (e.g. `rate(http_server_requests_count{service="student-api"}[5m])`).
- SymPy sidecar `/metrics` endpoint returns a payload that contains at least `cena_cas_verifications_total` counter.
- `docker-compose -f docker-compose.app.yml -f config/docker-compose.observability.yml up` boots cleanly with the new exporters.
- Runbook updated.
- No new test-only code paths in production source.

## Non-negotiable references

- Memory "No stubs — production grade" — every new target exposes real metrics, no stand-ins.
- Memory "Senior Architect mindset" — root cause (scrape-config drift), not symptom.
- Memory "Check container state before build" — verify compose is healthy before declaring done.
- Memory "Full sln build gate".
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) + [PRR-047](TASK-PRR-047-llm-prompt-cache-enforcement-hit-rate-slo.md) + [PRR-233](TASK-PRR-233-prompt-cache-slo-per-target.md) — downstream consumers of Redis metrics.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + Prometheus targets API output>"`

## Related

- EPIC-PRR-L.
- PRR-430 — k8s ServiceMonitor discovery replaces static_configs in prod.
- PRR-434 — alerting rules depend on the metrics this task exposes.
- PRR-436 — dashboards depend on complete scrape coverage.
