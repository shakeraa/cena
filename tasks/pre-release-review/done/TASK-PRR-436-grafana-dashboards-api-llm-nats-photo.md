# TASK-PRR-436: Four missing Grafana dashboards (API golden signals, LLM cost, NATS queue, photo pipeline)

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: persona-sre, persona-finops, persona-privacy (photo pipeline surface)
**Source docs**: [config/grafana/dashboards/](../../config/grafana/dashboards/) — currently contains only `cena-actors.json` + `ocr-cascade.json`
**Assignee hint**: kimi-coder + SRE + finops review
**Tags**: source=observability-audit-2026-04-22, epic=epic-prr-l, priority=p1, dashboards, grafana
**Status**: Blocked on PRR-433 (complete scrape targets) + PRR-435 (metrics + logs pipelines)
**Tier**: launch
**Epic**: [EPIC-PRR-L](EPIC-PRR-L-observability-completion.md)

---

## Why

Cena ships with two dashboards — `cena-actors.json` + `ocr-cascade.json`. Both are legitimate but narrow: one for Proto.Cluster + actor activation, one for the OCR cascade. Four dashboards are missing that an on-call engineer needs at 03:00 on Bagrut morning:

1. **API golden signals** — p50/p95/p99 latency, requests per second, error rate, saturation. Three services × four signals. Today: nothing. If student-api error rate is 6%, nobody sees it in a dashboard.
2. **LLM cost** — tokens/second, dollars-per-student-per-day, cache hit rate, per-tier distribution. The $3.30/student/month budget from [ADR-0050 Q5](../../docs/adr/0050-multi-target-student-exam-plan.md) has no visual. The [PRR-047 cache-hit SLO](TASK-PRR-047-llm-prompt-cache-enforcement-hit-rate-slo.md) and [PRR-233 per-target observability](TASK-PRR-233-prompt-cache-slo-per-target.md) emit metrics with no dashboard reading them.
3. **NATS queue depth + DLQ** — queue depth per subject, consumer lag, DLQ growth rate. `ALERT-QUEUE-001` watches queue depth > 100; when it fires, the first question is "which subject? which consumer?". A dashboard answers that; a log grep does not.
4. **Photo pipeline** — uploads per second, OCR latency by layer, CSAM rate, moderation verdicts. [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md) is an active launch-lane; without a dashboard, it ships blind.

The root cause is that dashboards were built one at a time as features landed and the "here are the visuals for Launch" holistic pass was skipped. This task does that pass.

Per memory "Honest not complimentary" — a dashboard that exists gives false confidence; no dashboard gives honest-blind. We're currently somewhere in between, which is the worst state.

## How

### `api-golden-signals.json`

Four panels × three services = 12 panels + one overview:

- **Overview panel**: a single-stat table per service showing current p95, current RPS, current error rate, up/down status. Color-coded (green/yellow/red) against SLO thresholds.
- **Latency panel per service**: time-series of p50/p95/p99. PromQL: `histogram_quantile(0.95, rate(http_server_request_duration_seconds_bucket{service="student-api"}[5m]))`. Annotations overlay deploy events (Prometheus `increase(process_start_time_seconds)`).
- **Requests panel per service**: RPS by endpoint (top 10 by volume). `rate(http_server_requests_count{service="student-api"}[5m])` grouped by route.
- **Errors panel per service**: 5xx rate + 4xx rate, stacked. Separate panel for auth-failure rate (401/403) to distinguish from infrastructure failures (500).
- **Saturation panel per service**: CPU, memory, DB connection pool usage, thread pool queue depth. `process_cpu_seconds_total` + `process_resident_memory_bytes` from the default ASP.NET Core exporter.

### `llm-cost.json`

Six panels:

- **$/student/day over time** — `sum(rate(cena_llm_cost_usd_total[1d])) / count(count by (student_id)(cena_llm_requests_total[1d]))`. Overlaid with the $3.30/month budget line (divided by 30).
- **Tokens/second** — stacked by model tier (Haiku / Sonnet / Opus), matching [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) three-tier routing. Shows whether tier selection is actually distributing cost the way the ADR assumes.
- **Cache hit rate** — per [PRR-047](TASK-PRR-047-llm-prompt-cache-enforcement-hit-rate-slo.md) SLO. Red line at 70% (the SLO floor). Per-target breakdown from [PRR-233](TASK-PRR-233-prompt-cache-slo-per-target.md).
- **Vision-model calls** — rate + cost, ties to [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md) photo-upload + HWR fallback paths.
- **Hint-generation tier distribution** — per [PRR-145](TASK-PRR-145-adr-hint-generation-model-tier-selection.md). Shows whether the hint-generation heuristic is pushing calls into the expensive tier more than it should.
- **Top 10 endpoints by LLM spend** — `topk(10, sum by (endpoint)(rate(cena_llm_cost_usd_total[5m])))`.

### `nats-queue.json`

Five panels:

- **Queue depth per subject** — `cena_nats_queue_depth{subject=~".+"}`. Threshold line at 100 (the `ALERT-QUEUE-001` fire threshold).
- **Consumer lag per consumer** — `cena_nats_consumer_lag_messages`. Shows which consumers are falling behind, not just overall backlog.
- **DLQ growth rate** — `rate(cena_nats_dlq_messages_total[5m])`. Non-zero = something broken; flat = healthy.
- **Publish rate by publisher** — ties to [NatsOutboxPublisher](../../src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs). Shows which service is pushing the most traffic.
- **Subject topology** — a Grafana Status History panel keyed by subject, colored by health status (active/stalled/dead). One visual glance answers "is anything broken?".

### `photo-pipeline.json`

Seven panels:

- **Uploads per second** — `rate(cena_photo_uploads_total[5m])`. Split by accepted / rejected.
- **OCR latency by layer** — Layer 0 through Layer 5 from ADR-0033. Histogram per layer. Identifies which OCR layer is the bottleneck at peak.
- **CSAM flag rate** — `rate(cena_photo_csam_flags_total[5m])`. **Must be near-zero.** Any non-zero value triggers `ALERT-CSAM-001`. A dashboard panel makes the rate visible pre-alert.
- **Moderation verdict distribution** — stacked bar: accepted / rejected / manual-review. Per [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md).
- **EXIF-strip failures** — rate per PRR-001. Should be flat-zero.
- **Vision-LLM call rate + cost** — ties back to the LLM cost dashboard but scoped to photo-diagnostic path. Supports the ≤5% fallback cap from [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md).
- **Per-student upload rate + hard-cap tracking** — for [PRR-400](TASK-PRR-400-per-tier-upload-counter.md) tier caps. Shows soft-cap breaches without yet alerting.

### Dashboards-as-code

All four dashboards are authored in JSON and version-controlled under `config/grafana/dashboards/`. Provisioning is via `config/grafana/provisioning/dashboards/dashboards.yml` which points at the directory. No hand-edits in the Grafana UI that survive only in the `grafana_data` volume — that path is how dashboards drift and lose their git history.

A CI check asserts every JSON file under `config/grafana/dashboards/` is valid Grafana JSON and uses only pre-provisioned datasources (Prometheus + Loki, not a dev's local ad-hoc datasource).

### Sign-off

- Persona-sre reviews the API golden signals + NATS dashboards and confirms: "if X breaks, I see it here first."
- Persona-finops reviews the LLM cost dashboard and confirms: "if we breach the $3.30/student/month budget, this dashboard surfaces it."
- Persona-privacy reviews the photo pipeline dashboard and confirms: no PII leaks into dashboard titles or labels.

## Files

- `config/grafana/dashboards/api-golden-signals.json` (new).
- `config/grafana/dashboards/llm-cost.json` (new).
- `config/grafana/dashboards/nats-queue.json` (new).
- `config/grafana/dashboards/photo-pipeline.json` (new).
- `config/grafana/provisioning/dashboards/dashboards.yml` — ensure the directory is auto-provisioned (may already exist; verify).
- `scripts/ops/validate-dashboards.sh` (new) — CI check for valid JSON + valid datasource references.
- `docs/ops/runbooks/observability-operations.md` — extend with "which dashboard for which incident" lookup table.

## Definition of Done

- All four new dashboards provision cleanly on `docker-compose -f config/docker-compose.observability.yml up` (no manual import step).
- Each panel returns non-error time-series data in Dev with any amount of ambient traffic (verified by PR screenshot per dashboard).
- `scripts/ops/validate-dashboards.sh` passes in CI.
- Sign-off recorded from persona-sre + persona-finops + persona-privacy.
- Runbook lookup table documents which dashboard to open for which alert.
- Full `Cena.Actors.sln` builds cleanly (no .NET changes expected, but the gate remains).

## Non-negotiable references

- Memory "No stubs — production grade" — real panels on real metrics, no "TBD" widgets.
- Memory "Honest not complimentary" — panels don't hide red with log-scale tricks or averaged-out time windows.
- Memory "Senior Architect mindset" — dashboards are a system (one for each major failure class), not a pile of widgets.
- Memory "Full sln build gate".
- [ADR-0050 Q5](../../docs/adr/0050-multi-target-student-exam-plan.md), [PRR-047](TASK-PRR-047-llm-prompt-cache-enforcement-hit-rate-slo.md), [PRR-233](TASK-PRR-233-prompt-cache-slo-per-target.md), [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md), [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md) — dashboards surface the concerns these raise.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + 4 dashboard screenshots + persona sign-offs>"`

## Related

- EPIC-PRR-L.
- PRR-433 (scrape completeness), PRR-434 (alerts visualized), PRR-435 (Loki logs queryable alongside metrics).
