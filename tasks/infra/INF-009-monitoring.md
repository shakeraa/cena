# INF-009: Grafana Cloud — OpenTelemetry, Dashboards, Alerts

**Priority:** P1 — blocks production observability
**Blocked by:** INF-006 (ECS)
**Estimated effort:** 2 days
**Contract:** `contracts/actors/supervision_strategies.cs` (telemetry metrics)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

All services export telemetry via OpenTelemetry Protocol (OTLP) to Grafana Cloud. Dashboards cover actor system health, LLM costs, learning session metrics, and infrastructure. Alerts fire on SLA breaches.

## Subtasks

### INF-009.1: OpenTelemetry Collector Configuration

**Files to create/modify:**
- `infra/terraform/modules/monitoring/otel-collector.tf`
- `config/otel/otel-collector-config.yaml`

**Acceptance:**
- [ ] OTEL Collector deployed as ECS sidecar
- [ ] Receives: traces (OTLP/gRPC), metrics (OTLP/gRPC), logs (OTLP/gRPC)
- [ ] Exports to: Grafana Cloud (Tempo for traces, Mimir for metrics, Loki for logs)
- [ ] Sampling: 100% for errors, 10% for successful requests (production)
- [ ] Service name propagation: `Cena.Actors`, `Cena.LLM.ACL`, `Cena.Web`

**Test:**
```bash
curl http://localhost:13133/  # OTEL collector health
# Assert: 200 OK
```

---

### INF-009.2: Grafana Dashboards

**Files to create/modify:**
- `config/grafana/dashboards/actor-system.json`
- `config/grafana/dashboards/llm-costs.json`
- `config/grafana/dashboards/learning-sessions.json`

**Acceptance:**
- [ ] Actor dashboard: active actors, message throughput, mailbox depth, supervision restarts, circuit breaker state
- [ ] LLM dashboard: cost per model, token usage, latency percentiles, cache hit rate, budget exhaustion rate
- [ ] Session dashboard: active sessions, questions/minute, accuracy trends, fatigue scores, methodology distribution

**Test:**
```bash
grafana-cli dashboards list
# Assert: 3 dashboards created
```

---

### INF-009.3: Alert Rules

**Files to create/modify:**
- `config/grafana/alerts/critical.yaml`
- `config/grafana/alerts/warning.yaml`

**Acceptance:**
- [ ] CRITICAL: actor restart rate > 10/min, circuit breaker open > 5 min, 5xx rate > 1%, database connection failures
- [ ] WARNING: LLM latency p99 > 10s, cache hit rate < 40%, daily cost > $100, memory usage > 80%
- [ ] Notification channels: Slack (#cena-alerts), PagerDuty (critical only)
- [ ] Alert deduplication: same alert not re-fired within 15 minutes

**Test:**
```yaml
# Verify alert fires on test metric
- alert: HighActorRestartRate
  expr: rate(cena_supervision_restarts_total[5m]) > 10
  for: 2m
  severity: critical
```

---

## Rollback Criteria
- Monitoring failure does not affect application; fallback to CloudWatch native dashboards

## Definition of Done
- [ ] OTLP collector running, traces visible in Grafana
- [ ] 3 dashboards deployed
- [ ] Alert rules tested with synthetic metrics
- [ ] PR reviewed by architect
