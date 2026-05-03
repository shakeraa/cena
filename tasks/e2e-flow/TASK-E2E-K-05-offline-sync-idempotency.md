# TASK-E2E-K-05: Offline sync idempotency (RDY-075)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-K](EPIC-E2E-K-offline-pwa.md)
**Tag**: `@offline @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/offline-sync-idempotency.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Offline queue has 10 answers → reconnect flushes → first batch partially fails (network mid-flush) → retry → end state: 10 answers recorded (not 20).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Client | Idempotency key per answer |
| Server | Dedup by key |
| Mastery | No double-count |

## Regression this catches

Double-count on retry; ghost answers (server has them, client thinks failed).

## Done when

- [ ] Spec lands
- [ ] Tagged `@p1`
