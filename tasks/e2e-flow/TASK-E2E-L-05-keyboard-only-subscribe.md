# TASK-E2E-L-05: Keyboard-only subscription flow

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@a11y @keyboard @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/keyboard-only-subscribe.spec.ts`

## Journey

Run [B-01](TASK-E2E-001-subscription-happy-path.md) using only keyboard — Tab, Enter, Space, Esc. Every actionable element reachable in logical order.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Tab order | Matches visual order |
| Focus indicator | Always visible (not `outline: 0`) |
| Skip link | Works |
| Modal | Focus trap correctly |

## Regression this catches

Focus lost after modal close; tab jumps into admin-only elements; skip-link broken.

## Done when

- [ ] Spec lands
