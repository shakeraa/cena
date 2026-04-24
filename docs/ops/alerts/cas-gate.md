# CAS Gate — Operational Alerts

Per ADR-0032 §15. Metrics exposed via the `Cena.Cas.Gate` OpenTelemetry meter (v1.0).

## Alert thresholds

| Metric | Type | Threshold | Severity | Runbook |
|--------|------|-----------|----------|---------|
| `cena_cas_startup_ok` | gauge | `!= 1` for ≥60s | page | Admin Host refused traffic; check `[CAS_STARTUP_FAIL]` logs. Likely: SymPy sidecar down, NATS unreachable, or CAS engine config drift. |
| `cena_questions_rejected_cas_total` | counter | `rate(1h) > 50` | warn | High CAS-rejection rate; check `[AI_GEN_CAS_REJECT]` / gate rejections for a content quality regression or LLM prompt drift. |
| `cena_cas_verify_latency_ms` | histogram | `p99 > 1500` for 5m | warn | CAS engines slowing. SymPy >300ms or NATS router >1000ms signals an engine or worker issue. |
| `cena_cas_circuit_open_total` | counter | `rate(5m) > 0` | warn | Circuit breaker tripped — CAS engine unhealthy. Check engine logs; gate will fall back to `Unverifiable` in Shadow, drop in Enforce. |
| `cena_cas_override_total` | counter | `rate(24h) > 5` per operator | info | Elevated operator override rate — may indicate systemic CAS false-positives worth root-causing. |

## On-call quick-reference

1. **Startup probe fails** → `kubectl logs ...admin-host... | grep CAS_STARTUP` → if `[CAS_STARTUP_FAIL]` persists after redeploy, downgrade to `CENA_CAS_GATE_MODE=Shadow` to restore traffic and open P1.
2. **Rejection rate spikes** → pull last 100 `[AI_GEN_CAS_REJECT]` lines; classify by `engine` + `reason`. If ≥80% are same `reason`, file a CAS engine bug.
3. **Latency spike** → check sidecar CPU and worker queue depth; SymPy single-thread saturates at ~60 rps per replica.

## Dashboards

Grafana dashboard JSON is deferred to post-pilot (ADR-0032 §Open items). Until then, ad-hoc PromQL queries against the metrics above are sufficient for the pilot cohort volume.
