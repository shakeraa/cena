# TASK-E2E-J-01: SymPy sidecar down → CAS gate fails configurably

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @cas @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cas-sidecar-down.spec.ts`

## Journey

Sidecar stopped mid-suite → LLM response triggers CAS verify → circuit breaker trips → admin UI shows `CasOracleDegraded`. Subject to `CENA_CAS_GATE_MODE` (enforce vs shadow).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Enforce mode | Student sees fallback; no math surfaces |
| Shadow mode | Student sees answer + shadow warning in admin only |
| Bus | `CasOracleDegradedV1` |

## Regression this catches

Enforce mode silently falls open; shadow mode doesn't warn admin.

## Done when

- [ ] Spec lands
- [ ] Both modes tested
- [ ] Tagged `@cas @p0`
