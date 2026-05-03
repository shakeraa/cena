# TASK-E2E-J-04: Firebase Auth down → existing sessions continue

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @auth @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/firebase-down.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Firebase emulator stopped → existing signed-in session continues (cached token) → new sign-ins fail fast with friendly error → emulator restarts → new sign-ins work.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Distinct UI for retryable vs permanent errors |
| Existing session | `/home` keeps rendering |
| Retry | Exponential backoff, no flood |

## Regression this catches

Existing session gets signed out on outage; retries hammer the emulator.

## Done when

- [ ] Spec lands
