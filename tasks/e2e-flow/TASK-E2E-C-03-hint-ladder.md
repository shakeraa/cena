# TASK-E2E-C-03: Wrong answer → hint ladder

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/hint-ladder.spec.ts`

## Journey

Same setup as C-02 but wrong answer → hint tier 1 surfaces (stem-grounded per PRR-262) → still wrong → tier 2 → still wrong → tier 3 (solution walkthrough) → student marks "I understand" → next question.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Hint cards increase in specificity; no reveal-then-hide violation |
| DB | `MisconceptionEventV1` session-scoped (NOT on StudentProfile per ADR-0003) |
| Bus | Misconception event has `session_id` populated, `student_id` NULL or opaque |
| Event-stream scan | Query `misconception_events WHERE student_id IS NOT NULL` → 0 rows |

## Regression this catches

Misconception data leaking to student profile (ADR-0003 ship blocker); hint ladder skipping tiers; hint content not stem-grounded (pulls cross-session context).

## Done when

- [ ] Spec lands
- [ ] Negative-property assertion (zero profile-scoped rows) in teardown
- [ ] Tagged `@ship-gate @p0`
