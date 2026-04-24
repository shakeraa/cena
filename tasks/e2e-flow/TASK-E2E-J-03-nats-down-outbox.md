# TASK-E2E-J-03: NATS down → outbox buffers events

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/nats-down-outbox.spec.ts`

## Journey

NATS stopped → session completes → events queue in NATS outbox → NATS restarts → outbox drains → downstream catches up.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | `NatsOutbox` rows accumulate during outage |
| Post-restart | Zero outbox rows; no duplicate deliveries |
| Eventual consistency | Window < 30s |

## Regression this catches

Outbox bypass (lost event); duplicates on restart; hard 500 to students during outage.

## Done when

- [ ] Spec lands
- [ ] Tagged `@p0`
