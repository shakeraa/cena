# EPIC-PRR-L: Observability completion — close the scrape/alerting/pipeline/dashboard gaps

**Priority**: P0 — launch-gate; paired with EPIC-PRR-K (load + chaos test exposes these gaps the moment it runs)
**Effort**: M-L (2-3 weeks aggregate across 4 sub-tasks; parallelizable)
**Lens consensus**: persona-sre (primary), persona-finops (cost observability), persona-privacy (log-handling + data residency)
**Source docs**:
- [src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs](../../src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs) — ActivitySources + CriticalAlerts as records
- [config/docker-compose.observability.yml](../../config/docker-compose.observability.yml) — Prometheus + Grafana + OTel Collector + Jaeger
- [config/prometheus.yml](../../config/prometheus.yml) — current scrape targets (incomplete)
- [config/otel-collector.yaml](../../config/otel-collector.yaml) — current pipeline (traces-only)
- [config/grafana/dashboards/](../../config/grafana/dashboards/) — existing cena-actors.json + ocr-cascade.json
- [ADR-0058 error aggregator Sentry](../../docs/adr/0058-error-aggregator-sentry.md) — complement, not replacement
**Assignee hint**: kimi-coder + SRE review; human-architect signs off on Alertmanager + notification channel
**Tags**: source=observability-audit-2026-04-22, type=epic, epic=epic-prr-l, sre, finops, launch-gate, no-stubs
**Status**: Not Started
**Tier**: launch
**Related epics**: [EPIC-PRR-K](EPIC-PRR-K-actor-cluster-capacity-validation.md) (load harness will emit metrics nothing currently receives — this epic's tasks must land first or concurrently)

---

## 1. Why this epic exists

The observability stack is ~70% wired. Prometheus, Grafana, Jaeger, OTel Collector, and OpenTelemetry instrumentation exist across 19 files in the .NET codebase. [CenaActivitySources](../../src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs) defines 6 critical-path tracing boundaries. [CriticalAlerts](../../src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs) defines 6 alert specifications.

But four concrete gaps make the stack lie about what it sees:

1. **Prometheus scrape-target gap.** `config/prometheus.yml` scrapes only `cena-actors-host` (port 5119) and `cena-admin-api` (port 5050). **Student API is absent** — the highest-traffic service has a `/metrics` endpoint that nothing pulls. SymPy sidecar, Redis, and Postgres are also unscraped. A dashboard that reads from Prometheus is painting half the truth.
2. **OTel Collector pipeline gap.** `config/otel-collector.yaml` has a single pipeline: `traces: [otlp] → [jaeger]`. **Metrics and logs pipelines do not exist in the collector**, so any metric exported via OTLP is dropped on the floor. Serilog writes structured logs; nothing ships them anywhere.
3. **Alert-wiring gap.** The 6 `CriticalAlert` records in code describe *what* should fire and when. They do not fire. There is no `alerts.yml` Prometheus ruleset, no Alertmanager, no PagerDuty/Slack/email route. On-call is blind until someone manually watches a Grafana tile.
4. **Dashboard gap.** Two dashboards exist (`cena-actors.json`, `ocr-cascade.json`). Six more are needed for a defensible launch: API golden signals, LLM cost, NATS queue depth + DLQ, photo pipeline, Postgres/Marten, Redis.

Per memory "Senior Architect mindset" — trace the failure mode, fix root cause. The root cause here is that the observability stack was wired piecemeal as features landed, never closed out as a system. Per memory "Honest not complimentary" — the current `cena-actors.json` dashboard looks green because it's only reading the metrics it scrapes, not because the system is green. That's dangerous.

The 03:00-on-Bagrut-morning failure mode this epic prevents: an issue on student-api (the hot path) is invisible to anything except manual log-tail. By the time anyone sees it, students are already locked out.

## 2. How the epic closes the gaps

Four focused sub-tasks. Each one is self-contained but sequencing matters because later tasks depend on earlier ones.

### PRR-433 — Prometheus scrape-target completeness

Add every service that exports metrics to `prometheus.yml` scrape targets: student-api, sympy-sidecar (via Python Prometheus client or sidecar exporter), redis (redis_exporter), postgres (postgres_exporter). Add service-labels for consistent querying. Verify every target returns 200 on `/metrics` in Dev and staging.

### PRR-434 — Alertmanager wiring + CriticalAlerts as Prometheus rules

Deploy Alertmanager alongside Prometheus. Translate each of the 6 `CriticalAlert` records from C# into a concrete Prometheus alerting rule with PromQL expressions + labels + annotations. Wire notification channels (Slack primary, email fallback; PagerDuty later if warranted). Add a self-test alert that intentionally fires in Dev to verify the pipeline. The C# `CriticalAlert` records become documentation sourced-from-code — a reconciliation test verifies every record has a matching Prometheus rule.

### PRR-435 — OTel Collector pipelines for metrics + logs

Extend `otel-collector.yaml` from traces-only to traces + metrics + logs. Metrics pipeline: `otlp → prometheus-remote-write` (so OTLP-emitted metrics show up alongside scrape-based metrics in the same Prometheus instance). Logs pipeline: `otlp → loki` (self-hosted Loki added to observability stack). Wire Serilog's OTLP log sink in Program.cs for all three service hosts so structured logs flow end-to-end. Trace-logs-metrics correlation via `trace_id` + `span_id` labels on every log entry.

### PRR-436 — Four missing Grafana dashboards

Build + provision: `api-golden-signals.json` (p50/p95/p99 per endpoint, RPS, error rate, saturation — all three services), `llm-cost.json` (tokens/second, $ per student per day, cache hit rate, per-tier routing distribution — ties to [PRR-233](TASK-PRR-233-prompt-cache-slo-per-target.md)), `nats-queue.json` (queue depth, consumer lag, DLQ growth), `photo-pipeline.json` (uploads/sec, OCR latency, CSAM rate, moderation verdicts — ties to EPIC-PRR-J). All four provisioned via `config/grafana/dashboards/` (version-controlled JSON, not hand-edited in the UI).

## 3. Non-negotiables (architect guardrails)

- **No stubs.** Every scrape target is a real `/metrics` endpoint backed by real instrumentation. Every alerting rule fires on real PromQL against real metrics, not synthetic stand-ins. Every dashboard panel reads from a real datasource.
- **Version-controlled configuration.** Prometheus rules, Alertmanager config, OTel Collector pipeline, Grafana dashboards all live in `config/` and are reviewable in PR diff. No hand-edits in the Grafana UI that survive only in `grafana_data` volume.
- **Reconciliation tests.** A Cena.Actors.Tests architecture test walks the 6 `CriticalAlert` records and asserts each has a matching Prometheus rule in `config/alerts.yml`. If a developer adds a new `CriticalAlert` record without a rule, the build fails. Per memory "Senior Architect mindset" — don't let docs-as-code drift from ops-as-code.
- **Full `Cena.Actors.sln` builds cleanly.** Per memory 2026-04-13.
- **Self-test alert fires in Dev.** Anyone can confirm the notification chain works end-to-end by triggering a single metric change; if the alert doesn't reach Slack within 2 minutes, the pipeline is broken.
- **PPL Amendment 13 alignment.** Logs may contain PII; log pipeline respects the existing [PiiDestructuringPolicy + SessionRiskLogEnricher](../../src/actors/Cena.Actors.Host/Program.cs#L84-L91) scrubbing before shipping to Loki. No raw student data leaves the service process unscrubbed.
- **Measurement replaces assertion.** This epic produces green/red numbers on every gap listed in §1. No subjective sign-off.

## 4. Sub-task table

| ID | Title | Priority | Effort |
|---|---|---|---|
| [PRR-433](TASK-PRR-433-prometheus-scrape-completeness.md) | Prometheus scrape-target completeness (student-api + sympy + redis + postgres) | P0 | S (2-3 days) |
| [PRR-434](TASK-PRR-434-alertmanager-criticalalerts-rules.md) | Alertmanager + the 6 `CriticalAlert` records as Prometheus rules + notification channel | P0 | M (1-2 weeks) |
| [PRR-435](TASK-PRR-435-otel-collector-metrics-logs-pipelines.md) | OTel Collector pipelines for metrics + logs (adds Loki to stack) | P0 | M (1 week) |
| [PRR-436](TASK-PRR-436-grafana-dashboards-api-llm-nats-photo.md) | Four missing Grafana dashboards (API golden signals, LLM cost, NATS queue, photo pipeline) | P1 | M (1 week) |

Execution order:
- PRR-433 + PRR-435 can run in parallel — they don't touch each other.
- PRR-434 blocks on PRR-433 (alerts need complete scrape targets to have real expressions).
- PRR-436 blocks on PRR-433 + PRR-435 (dashboards read from Prometheus + Loki after both pipelines are healthy).

## 5. Definition of Done (epic-level, verifiable not subjective)

1. `curl http://prometheus:9090/api/v1/targets | jq '.data.activeTargets | length'` returns ≥ 6 (actor-host + admin-api + student-api + sympy + redis + postgres; more as needed).
2. Every `CriticalAlert` record in `ObservabilityConfiguration.cs` has a corresponding rule in `config/alerts.yml`. Reconciliation test passes.
3. Triggering a test metric change fires a Slack notification within 2 minutes in Dev + staging.
4. `curl http://otel-collector:13133/health` returns healthy; both metrics + logs pipelines active. A log emitted via Serilog reaches Loki within 5 seconds.
5. Six Grafana dashboards exist under `config/grafana/dashboards/` and provision cleanly on a fresh `docker-compose -f config/docker-compose.observability.yml up`.
6. `dotnet build src/actors/Cena.Actors.sln` completes with zero warnings + zero errors.
7. Runbook updates in `docs/ops/runbooks/observability-operations.md` cover: silence an alert, add a scrape target, add a dashboard, investigate a pipeline outage.
8. Persona-sre sign-off on the 03:00-Bagrut-morning question: "if student-api p99 latency spikes, who knows within 5 minutes?" Answer must be: "Alertmanager → Slack → on-call."

## 6. Non-negotiable references

- Memory "No stubs — production grade".
- Memory "Senior Architect mindset" — root cause + reconciliation.
- Memory "Honest not complimentary" — dashboards must show truth, not what's convenient.
- Memory "Full sln build gate" (2026-04-13).
- Memory "Check container state before build" (2026-04-19).
- [ADR-0058](../../docs/adr/0058-error-aggregator-sentry.md) — Sentry complements; Sentry is error aggregation, this epic is metrics + alerts + logs + traces.
- [EPIC-PRR-K](EPIC-PRR-K-actor-cluster-capacity-validation.md) — load + chaos tests consume this epic's output.

## 7. Out of scope (intentional)

- Multi-region observability replication — single-region Launch.
- Commercial SaaS migration (Datadog, Honeycomb, Grafana Cloud) — Launch stays self-hosted for PPL A13 data-residency alignment. Revisit Post-Launch if ops cost justifies.
- Real User Monitoring (RUM) for the Vue PWA — separate concern; belongs in a Frontend-observability epic if/when prioritized.
- APM-level code profiling (Datadog Continuous Profiler, dotTrace) — not a Launch requirement.
- PagerDuty integration — Slack + email at Launch; PagerDuty added when on-call rotation formalizes.

## 8. Reporting

Epic closes when all 4 sub-tasks close AND §5 acceptance checks pass.

## 9. Related

- Depends on: nothing external (existing observability compose stack is present).
- Feeds: [EPIC-PRR-K](EPIC-PRR-K-actor-cluster-capacity-validation.md) — PRR-431 load harness + PRR-432 chaos simulation will exercise this stack.
- Complements: [ADR-0058](../../docs/adr/0058-error-aggregator-sentry.md) Sentry error aggregator.
