# TASK-E2E-G-05: Question moderation queue

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @content @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/moderation-queue.spec.ts`

## Journey

Student reports "this question is wrong" → admin moderation queue shows report → admin reviews → marks "invalid" → question pulled from active pool.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Queue listing + review UI |
| DB | `QuestionModerationEvent` recorded |
| Bus | `QuestionInvalidatedV1` |
| Student-side | Pool excludes the question going forward; running sessions broadcast update |

## Regression this catches

Invalid question still served; moderation action not broadcast to running sessions.

## Done when

- [ ] Spec lands
