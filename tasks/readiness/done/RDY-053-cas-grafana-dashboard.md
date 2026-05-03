# RDY-053: CAS Gate Grafana Dashboard JSON

- **Priority**: Medium — ops visibility
- **Complexity**: Low
- **Effort**: 2-4 hours

## Problem

Alerts live in `docs/ops/alerts/cas-gate.md`; the Grafana JSON (`ops/grafana/cas-gate.json`) was never created. Oncall has metrics but no visualization.

## Scope

- Panel: verification total (stacked by result/engine)
- Panel: verification duration p50/p95/p99 (per engine)
- Panel: circuit breaker state gauge
- Panel: rejection rate (1h rolling)
- Panel: binding coverage ratio (RDY-040 gauge)
- Panel: override total

## Acceptance

- [ ] JSON file at `ops/grafana/cas-gate.json`
- [ ] Imports cleanly to Grafana 10+
