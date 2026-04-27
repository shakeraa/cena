# TASK-E2E-J-08: Per-student rate limit kicks in (RATE-001)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/rate-limit-per-student.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Student fires >N requests / 10s → rate limiter rejects with 429 + Retry-After → UI shows "slow down" toast → student waits → requests succeed again.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | Rate-limit bucket state |
| DOM | Toast shown |
| Admin | Dashboard reflects tripping |

## Regression this catches

Limit not per-student (global limiter could DoS one student for whole tenant); limiter leaks cross-tenant.

## Done when

- [ ] Spec lands
