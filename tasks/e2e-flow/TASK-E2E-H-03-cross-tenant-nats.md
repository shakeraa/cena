# TASK-E2E-H-03: Events published by A are not delivered to B's NATS subscribers

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @compliance @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cross-tenant-nats.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped; required to assert cross-tenant subscriber receives 0 events)

## Journey

Actor-host publishes `cena.events.student.{studentId-in-A}.mastery-updated` → institute B's parent-digest aggregator (on same NATS cluster) does NOT receive.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Subject naming | Scoped by tenant |
| Subscriber filter | Includes tenant predicate |
| Count | Cross-tenant listener receives 0 events in the window |

## Regression this catches

Parent-digest ingests cross-tenant events (worst case: wrong family sees wrong kid's numbers).

## Done when

- [ ] Spec lands
- [ ] Tagged `@compliance @p0`
