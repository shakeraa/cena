# TASK-E2E-L-07: High-contrast + WCAG AA across flows

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@a11y @wcag @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/high-contrast-wcag.spec.ts`

## Journey

User enables a11y toolbar → high-contrast mode on → primary-color ratchet satisfies WCAG AA on every flagship screen (session, pricing, parent dashboard).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| axe | `wcag2aa` clean on each landing page |
| Vuexy | Primary color locked (`#7367F0`) — use pattern not alternate |
| Color-only info | None (icon + color pair) |

## Regression this catches

Contrast regression on new page; a11y toolbar bypass; color-only feedback slips in.

## Done when

- [ ] Spec lands
- [ ] Tagged `@wcag @p1`
