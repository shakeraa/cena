# RDY-052: SymPy Sidecar SIGKILL Chaos Test

- **Priority**: Medium — pre-pilot signal
- **Complexity**: Mid-senior engineer
- **Effort**: 1 day

## Problem

RDY-036 §10 required `tests/Cena.Chaos/SymPyKillTest.cs` — SIGKILL the sidecar mid-batch, assert circuit breaker opens within 10s, remaining questions fall through to `NeedsReview=true`. Deferred in ADR-0032.

## Scope

- Integration test that starts SymPy sidecar, drives 50 verify calls, SIGKILLs mid-stream
- Assert circuit breaker opens within 10s
- Assert remaining verifications return `CasGateOutcome.CircuitOpen` + binding.Status == `Unverifiable`
- Assert no exception leaks to callers

## Acceptance

- [ ] Test exists and passes reliably in nightly
