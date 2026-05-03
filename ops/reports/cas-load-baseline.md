# CAS Gate Load Baseline (RDY-051)

**Status**: workflow wired, awaiting first green run against real infra.

## Target SLOs (RDY-036 §10)

- 100 concurrent VUs
- Sustained 2 min
- p95 latency < 3000 ms
- `http_req_failed` rate < 1%
- CAS circuit breaker stays closed throughout

Thresholds are encoded inside `tests/load/cas-gate-load.js` — k6 exits
non-zero when any is breached. The nightly workflow `.github/workflows/
cas-load-nightly.yml` runs at 03:47 UTC.

## First-run checklist

Before flipping the k6 run from "skipped" to "fires traffic", these
GitHub secrets must be present on the repo:

- `CENA_ADMIN_URL` — base URL of the staging Admin API
- `CENA_ADMIN_TOKEN` — bearer token for a super-admin service account
  with the `api/admin/questions` scope

Once both secrets are set, the nightly resolves them, runs the k6
scenario, and uploads `ops/reports/cas-load-summary.json` as a workflow
artifact.

## Measured baseline

_Awaits first green nightly. Populate this section with:_

| Metric | Target | Measured | Date |
|--------|--------|----------|------|
| p50 latency | < 1000 ms | TBD | — |
| p95 latency | < 3000 ms | TBD | — |
| p99 latency | — | TBD | — |
| http_req_failed | < 1% | TBD | — |
| Peak VUs sustained | 100 | TBD | — |
| CAS breaker state | closed | TBD | — |

## Escalation

- If measured p95 ≥ 3000 ms: open a follow-up ticket with the k6
  summary attached; do not flip `CENA_CAS_GATE_MODE=Enforce` in
  production until the regression is resolved.
- If `http_req_failed` ≥ 1%: inspect the Admin API logs for 5xx
  patterns before treating it as a CAS-specific fault.
