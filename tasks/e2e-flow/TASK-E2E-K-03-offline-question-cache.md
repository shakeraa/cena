# TASK-E2E-K-03: Offline question cache

**Status**: Proposed
**Priority**: P2
**Epic**: [EPIC-E2E-K](EPIC-E2E-K-offline-pwa.md)
**Tag**: `@offline @pwa @p2`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/offline-question-cache.spec.ts`

## Journey

Student's current plan questions pre-cached by SW → offline nav to next question works from cache → fresh questions NOT cached → those show offline-unavailable UI.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| SW cache | Asset vs data cache names separate |
| Freshness | Cache invalidated on plan update |

## Regression this catches

Cache stale after plan regen; cache grows unbounded; admin-only routes polluting offline cache.

## Done when

- [ ] Spec lands
