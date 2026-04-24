# TASK-E2E-F-02: "Assign 15 min" homework from heatmap

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-F](EPIC-E2E-F-teacher-classroom.md)
**Tag**: `@teacher @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/assign-homework.spec.ts`

## Journey

Teacher picks a struggling topic on heatmap → "Assign 15 min" → picks classroom roster → emits `HomeworkAssignedV1` → student's next session surfaces as priority.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Modal + roster picker |
| DB | `HomeworkAssignment` row |
| Bus | `HomeworkAssignedV1` |
| Student-side | Priority bucket updated on `/home` |

## Regression this catches

Assigned to wrong classroom; student's bucket not updated; out-of-scope classroom accepted.

## Done when

- [ ] Spec lands
