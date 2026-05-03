# RDY-015: Accessibility Sweep (ARIA, Skip Link, Touch Targets, Focus)

- **Priority**: High — Israeli law requires accessible educational platforms
- **Complexity**: Mid frontend engineer
- **Source**: Expert panel audit — Tamar (A11y)
- **Tier**: 2
- **Effort**: 2 sprints (Sprint 1: 3 days ship-blocking, Sprint 2: 1 week quality)

> **Cross-review (Tamar)**: Split into 2 sprints. Sprint 1 is compliance-critical for Israeli law (skip link, aria-live, touch targets). Sprint 2 is quality polish (form ARIA, focus management, contrast, heading hierarchy).

## Problem

Multiple WCAG 2.1 AA violations found across the student app:
1. No skip link (`<a href="#main">Skip to main content</a>`)
2. No `aria-live` regions for dynamic content (hints, feedback, notifications)
3. Buttons default to 38px (below 44px WCAG 2.2 AA minimum)
4. No `aria-describedby`, `aria-required`, `aria-invalid` on form fields
5. No focus restoration after modal close
6. Only primary color (#7367F0) contrast-tested; secondary/success/error untested
7. No heading hierarchy validation tests
8. No `prefers-contrast` high-contrast mode support

## Scope

### 1. Skip link

Add `<a href="#main" class="skip-link">Skip to main content</a>` as first focusable element in the app shell. Visually hidden until focused.

### 2. ARIA live regions

Add `aria-live="polite"` on:
- Hint reveal panel (when hint appears)
- Answer feedback (correct/incorrect + XP)
- Notification toasts
- Session progress updates
- Error messages

### 3. Touch targets

Override Vuetify button minimum height to 44px in global CSS. Ensure all interactive elements (chips, toggles, icon buttons) meet 44x44px minimum.

### 4. Form field ARIA

Add `aria-required`, `aria-invalid`, `aria-describedby` to all VTextField instances. Link error messages via `aria-describedby` ID.

### 5. Focus management

- After modal close: return focus to trigger element
- Tab trap in open modals/dialogs
- `aria-current="page"` on active nav links

### 6. Color contrast testing

Extend `color-contrast.spec.ts` to test secondary, success, warning, error colors against light/dark backgrounds.

### 7. Heading hierarchy test

Add automated test: every page has exactly one `<h1>`, headings don't skip levels (no h3 before h2).

## Files to Modify

- `src/student/full-version/src/App.vue` — add skip link
- `src/student/full-version/src/components/session/QuestionCard.vue` — aria-live on feedback
- `src/student/full-version/src/components/session/HintPanel.vue` — aria-live
- `src/student/full-version/src/assets/styles/` — touch target overrides
- `src/student/full-version/src/components/auth/EmailPasswordForm.vue` — form ARIA
- `src/student/full-version/tests/a11y/color-contrast.spec.ts` — extend color tests
- New: `src/student/full-version/tests/a11y/heading-hierarchy.spec.ts`

## Sprint 1 — Ship-Blocking (3 days, Tier 0 compliance)

- [ ] Skip link present, visually hidden, visible on focus, navigates to main
- [ ] All dynamic content updates announced via `aria-live`
- [ ] All interactive elements >= 44x44px
- [ ] `prefers-reduced-motion` respected on all animations

## Sprint 2 — Quality (1 week, Tier 2)

- [ ] Form fields have `aria-required`, `aria-invalid`, `aria-describedby`
- [ ] Focus returns to trigger after modal close
- [ ] Color contrast tests cover all semantic colors (not just primary #7367F0)
- [ ] Heading hierarchy validated (one h1, no skipped levels)
- [ ] `prefers-contrast` high-contrast mode support
- [ ] All CSS animations wrapped in `@media (prefers-reduced-motion: reduce)`
- [ ] E2E test verifies animations disabled when `prefers-reduced-motion: reduce` is set
