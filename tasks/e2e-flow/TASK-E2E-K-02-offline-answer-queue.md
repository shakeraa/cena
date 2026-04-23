# TASK-E2E-K-02: Offline session answer queue

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-K](EPIC-E2E-K-offline-pwa.md)
**Tag**: `@offline @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/offline-answer-queue.spec.ts`

## Journey

Student signed in → goes offline → continues session → answers queued locally → goes online → queue flushes to backend → mastery catches up.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Offline indicator; answers visually accepted |
| localStorage | Queue grows while offline |
| Backend | Receives queue on reconnect; no duplicate answers (idempotency key) |

## Regression this catches

Queue drops on reload; duplicates on flush; offline indicator wrong.

## Done when

- [ ] Spec lands
- [ ] Tagged `@p1`
