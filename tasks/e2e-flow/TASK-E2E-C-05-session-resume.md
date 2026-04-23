# TASK-E2E-C-05: Session interrupt & resume

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @resilience @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/session-resume.spec.ts`

## Journey

Mid-session → close tab / drop connection → reopen within 30 min → session resumes at correct question with prior answers preserved.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| localStorage | Session state persisted |
| API | `/api/sessions/{id}/state` endpoint returns correct resume point |
| DB | SessionAggregate rehydrated from event stream |

## Regression this catches

Resumed session loses prior answers; state reset to Q1 (annoying); session lock prevents second device.

## Done when

- [ ] Spec lands
- [ ] 30-min-out-of-window resume → fresh session start (positive)
- [ ] Cross-device handoff tested (laptop → phone)
