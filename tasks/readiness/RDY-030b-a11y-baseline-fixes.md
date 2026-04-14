# RDY-030b: Accessibility Baseline Fixes (detected by RDY-030 rules)

- **Priority**: Medium — RDY-030 rules caught real gaps; fix to flip CI from advisory → blocking
- **Complexity**: Frontend engineer
- **Source**: RDY-030 self-test output
- **Tier**: 2
- **Effort**: 3-5 days
- **Depends on**: RDY-030 (automation, done)
- **Parent**: RDY-030

## Problem

The RDY-030 a11y automation ship introduced 4 new static rules that detected baseline violations already present in the codebase. CI for `test:a11y` is currently advisory (`continue-on-error: true`) until these are fixed. Flipping CI to blocking requires resolving:

### 1. `reduced-motion-coverage` — 18 components missing prefers-reduced-motion guards

Components with CSS animations/transitions that lack `@media (prefers-reduced-motion: reduce)`:

- `src/@layouts/components/TransitionExpand.vue`
- `src/@layouts/components/VerticalNav.vue`
- `src/@layouts/components/VerticalNavLayout.vue`
- `src/components/ConnectionStatus.vue`
- `src/components/MasteryMap.vue`
- `src/components/OnboardingCatalogPicker.vue`
- `src/components/common/StudentSkeletonCard.vue`
- `src/components/home/QuickActions.vue`
- `src/components/knowledge/ConceptTile.vue`
- `src/components/onboarding/LanguagePicker.vue`
- `src/components/onboarding/RoleSelector.vue`
- `src/components/progress/TimeBreakdownChart.vue`
- `src/components/session/FigureThumbnail.vue`
- `src/components/session/QuestionCard.vue`
- `src/components/session/WorkedExamplePanel.vue`
- `src/components/tutor/TutorThreadListItem.vue`
- (+ 2 more)

Fix: add this block to each component's `<style>`:

```css
@media (prefers-reduced-motion: reduce) {
  .animated-element {
    animation: none !important;
    transition: none !important;
  }
}
```

Or, better, add a global reset in `src/assets/styles/reduced-motion.css` and import once.

### 2. `aria-live-on-dynamic` — 3 components missing aria-live region

- `src/components/session/QuestionCard.vue` — dynamic hint/progress updates need aria-live
- `src/components/session/AnswerFeedback.vue` — correct/incorrect announcement needs aria-live (WCAG 4.1.3). Verified in test at `tests/unit/AnswerFeedback.a11y.spec.ts` (currently .skip — un-skip after fix).
- `src/components/notifications/NotificationListItem.vue` — new notification arrival needs aria-live

Fix: wrap the dynamically-updating region with `aria-live="polite"` (status) or `role="status"`.

### 2b. QuestionCard a11y spec blocked on dompurify import

The component-level axe spec `tests/unit/QuestionCard.a11y.spec.ts` is `.skip`-wrapped because `QuestionFigure.vue` has an unresolved `dompurify` import that breaks module loading. Same issue codex-coder reported during RDY-002 review. Fix the import (add `dompurify` dep or switch to browser-native `DOMPurify` global), then remove the `.skip` wrappers.

### 3. `math-ltr-wrapper` — 1 dev probe page missing bidi wrapper

- `src/pages/_dev/probe.vue` — dev-only, but RDY-030 rule flags any math rendering without wrapper

Fix: wrap the katex render in `<bdi dir="ltr">...</bdi>` even in dev pages, OR exclude `_dev/*` from the rule (add to ignore list in `math-ltr-wrapper.spec.ts`).

### 4. Component axe tests — establish baseline

Once baseline is clean, flip `component-axe` and `e2e-axe` jobs in `.github/workflows/a11y-tests.yml` from `continue-on-error: true` to `continue-on-error: false`.

## Acceptance Criteria

- [ ] All 18 components with animations have `prefers-reduced-motion` guards
- [ ] QuestionCard + NotificationListItem expose aria-live regions
- [ ] Math LTR wrapper rule passes (either fix probe or add exclusion)
- [ ] `npm run test:a11y` returns 0 failures
- [ ] `.github/workflows/student-web-ci.yml` A11y step has `continue-on-error` removed
- [ ] `.github/workflows/a11y-tests.yml` static-rules job has `continue-on-error` removed

## Files to Modify

- 18 component .vue files (add prefers-reduced-motion CSS)
- `src/components/session/QuestionCard.vue` (add aria-live)
- `src/components/notifications/NotificationListItem.vue` (add aria-live)
- `src/pages/_dev/probe.vue` OR `tests/a11y/math-ltr-wrapper.spec.ts` (exclude dev)
- `.github/workflows/student-web-ci.yml` (remove continue-on-error)
- `.github/workflows/a11y-tests.yml` (remove continue-on-error on static-rules)
