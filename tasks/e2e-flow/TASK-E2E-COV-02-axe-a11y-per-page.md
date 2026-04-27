# TASK-E2E-COV-02: axe-core a11y sweep per page (admin + student)

**Status**: Proposed
**Priority**: P0 (Israeli law compliance — Equal Rights for Persons with Disabilities Law 5758-1998 + Accessibility Regulations 5773-2013)
**Epic**: Coverage matrix
**Tag**: `@coverage @a11y @p0 @legal`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-X-axe-a11y-sweep.spec.ts` (new)
**Prereqs**: `npm i -D @axe-core/playwright` in `src/student/full-version/`

## Why this exists

The accessibility-statement page references the Israeli accessibility law and ADR commitments (color-blind filters, dyslexia-friendly font, screen-reader navigation, RTL). EPIC-L spec (i18n / RTL) covers locale + direction; this task adds the **automated WCAG 2.1 AA scan** that catches the boring-but-load-bearing class of violations: missing `alt`, missing `aria-label` on icon-only buttons, color contrast failures, form fields without `<label>`, etc.

Currently we have no automated a11y enforcement. Each page is one screen-reader-only-user-bug-report away from a regulator complaint.

## Journey

Driven by `@axe-core/playwright`:

1. Sign in (admin + student variants run in parallel).
2. For each route in the smoke matrix: `await injectAxe(page); await checkA11y(page)`.
3. Configure the rule set to WCAG 2.1 AA + best-practice. Disable `region` rule if the SPA's main landmark detection is intentional.
4. Group violations by impact (`critical` / `serious` / `moderate` / `minor`).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| WCAG 2.1 AA | 0 `critical` violations per route; 0 `serious` violations not in known-broken allowlist |
| Color contrast | Foreground/background contrast ratio ≥ 4.5:1 for body text, ≥ 3:1 for large text |
| Forms | Every input has a `<label>` (visible or `aria-label`) — `label` rule cannot regress |
| Icon-only buttons | Every icon-only button has `aria-label` |
| RTL | When `document.dir === 'rtl'` axe still passes (run scan in en + ar + he) |

## Regression this catches

- A new admin form added without `<label>` for one of its fields
- A Vuetify icon button (`<VBtn icon>`) shipped without `aria-label`
- A heading-level skip (h1 → h3, h2 missing) introduced when a section moves
- A new color token chosen from the design system that regresses contrast against the background it sits on
- Israeli-mandated accessibility-toolbar selector hijacked by another component (z-index / pointer-events regression)

## Done when

- [ ] `EPIC-X-axe-a11y-sweep.spec.ts` runs against every signed-in admin + student route
- [ ] WCAG 2.1 AA serial scan passes 0 critical / 0 serious; allowlist documents intentional exceptions with reason
- [ ] Same scan re-runs in ar + he locale (RTL path)
- [ ] Per-route violations dropped as JSON artifact + summary in test annotation
- [ ] CI gate fails build on any new critical violation
- [ ] Tagged `@a11y @p0`
