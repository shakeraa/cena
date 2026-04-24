# TASK-E2E-C-02: Practice session — happy path

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/practice-session-happy.spec.ts`

## Journey

`/home` → "Start practice" → `/session/{id}` → Q1 loaded (CAS-verified, stem-grounded) → student answers correctly → no hint ladder → mastery updates → Q2 → session ends → `/progress` reflects uptick.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Q renders with KaTeX LTR inside RTL (if locale=he/ar); correct feedback shown |
| DB | `AnswerSubmittedEventV1` sourced; BKT parameter updated; session state = Complete |
| Bus | `AnswerSubmittedV1`, `MasteryUpdatedV1` fired |
| SignalR | Live progress push received |

## Regression this catches

Question served un-verified by CAS (ADR-0002 ship blocker); BKT not updating; session never terminates.

## Done when

- [ ] Spec lands
- [ ] Runs < 90s
- [ ] Tagged `@learning @p0`
