# RDY-051: CAS Gate — k6 Load Test

- **Priority**: Medium — pre-pilot signal
- **Complexity**: Mid-senior engineer + k6
- **Effort**: 1 day

## Problem

RDY-036 §10 required `tests/Cena.Load/CasGateLoadTests.cs` at 100 concurrent creates, p95 < 3s, failure < 1%. Deferred in ADR-0032 "Open items" — needs an owner before pilot.

## Scope

- k6 script at `tests/load/cas-gate-load.js` (or C# `Cena.Load` project)
- 100 concurrent creators driving `POST /api/admin/questions`
- Assert p95 < 3s, failure < 1%, circuit-breaker closed throughout
- Wire into `.github/workflows/backend-nightly.yml`

## Acceptance

- [ ] Script exists and runs locally against docker-compose
- [ ] Nightly workflow invokes the script
- [ ] Baseline run results recorded in ops doc
