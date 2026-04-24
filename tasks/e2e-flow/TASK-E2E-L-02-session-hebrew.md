# TASK-E2E-L-02: Full session flow in Hebrew

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@i18n @rtl @ship-gate @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/session-hebrew.spec.ts`

## Journey

Locale = `he` → run [C-02](TASK-E2E-C-02-practice-session-happy.md) → math equations LTR inside RTL question bodies → hints RTL → feedback RTL.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Math | Every math segment inside `<bdi dir="ltr">` |
| KaTeX | Operators not mirrored |
| Question number | Correct reading direction |

## Regression this catches

Math mirrored (notorious reversed-equation bug); hint card direction wrong; question number swapped.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p1` — visual regression on math is user-facing
