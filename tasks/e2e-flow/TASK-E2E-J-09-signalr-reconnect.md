# TASK-E2E-J-09: SignalR reconnection (hub drop mid-session)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @realtime @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/signalr-reconnect.spec.ts`

## Journey

Student mid-session → SignalR drops (network blocker) → SPA auto-reconnects with last-seen message id → missed events replay → session continues.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | "Reconnecting..." toast then normal |
| Server | Last-seen-message-id honored; no duplicate events |
| No 401 | Token refresh on reconnect |

## Regression this catches

Session hangs after reconnect; duplicate events cause UI thrash; 401 loop on reconnect.

## Done when

- [ ] Spec lands
