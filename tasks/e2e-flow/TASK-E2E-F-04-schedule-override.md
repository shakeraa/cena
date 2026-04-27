# TASK-E2E-F-04: Schedule override (ADR-0044)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-F](EPIC-E2E-F-teacher-classroom.md)
**Tag**: `@teacher @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/schedule-override.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Teacher changes classroom schedule for next week (holiday, exam prep) → students see updated plan cadence on `/home`.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Student-side shows new plan |
| DB | `ScheduleOverride` row |
| Bus | `ScheduleOverriddenV1` |
| Regen | Plan regeneration respects override on next materialization |

## Regression this catches

Override applied to wrong classroom; regen lag > 24h; doesn't propagate to parent dashboard.

## Done when

- [ ] Spec lands
