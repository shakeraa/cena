# TASK-E2E-L-06: Screen-reader-friendly session flow

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@a11y @screen-reader @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/screen-reader-session.spec.ts`

## Journey

Run [C-02](TASK-E2E-C-02-practice-session-happy.md) with aria-live polite regions — correct/wrong feedback, hint surfacing, session summary all announced.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| aria-live | Regions present; correct politeness level |
| aria-hidden | Not on interactive elements |

## Regression this catches

aria-live swallowed by stacking wrapper; politeness level wrong; interactive element inside aria-hidden.

## Done when

- [ ] Spec lands
