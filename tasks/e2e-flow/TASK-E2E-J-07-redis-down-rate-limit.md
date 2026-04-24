# TASK-E2E-J-07: Redis down → rate limiter fails closed

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @security @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/redis-down-rate-limit.spec.ts`

## Journey

Redis stopped → rate-limit check fails → policy: fail-closed (no reads/writes) → students see 503 with Retry-After, not infinite spinner.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Backend | 503 + Retry-After header |
| Ops | Alert fired |
| Fail-mode | Closed, not open |

## Regression this catches

Fail-open (DoS vector — unlimited requests until Redis restored); silent hang.

## Done when

- [ ] Spec lands
