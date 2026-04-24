# TASK-E2E-C-06: Mastery trajectory visible on /progress

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @ship-gate @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/mastery-trajectory.spec.ts`

## Journey

Over N sessions (IClock-seamed fast-forward) → `/progress` → trajectory graph updated with BKT + HLR decay (MST-003, MST-008) → trajectory reflects actual performance, not flat-lined or jittery.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Graph renders with data points matching session outcomes |
| DB | `LearningSessionQueueProjection` + mastery snapshot reflect sessions |
| Ship-gate | NO banned terms — "streak", "days in a row", variable-ratio copy (GD-004) |

## Regression this catches

Trajectory frozen (projection lag); recent wins invisible; banned engagement copy slips in.

## Done when

- [ ] Spec lands
- [ ] Ship-gate shipgate-scanner regex runs inline
- [ ] Tagged `@ship-gate @p1`
