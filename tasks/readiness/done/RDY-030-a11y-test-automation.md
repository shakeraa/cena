# RDY-030: Accessibility Test Automation

- **Priority**: High — prevents a11y regressions after RDY-015 sweep
- **Complexity**: Mid frontend engineer
- **Source**: Cross-review — Tamar (A11y)
- **Tier**: 2
- **Effort**: 3-5 days

## Problem

RDY-015 fixes current a11y violations, but without automated testing, regressions will reappear within weeks. New components will ship without ARIA attributes, touch targets will shrink, heading hierarchies will break. The sweep is wasted effort without guardrails.

## Scope

### 1. axe-core integration in E2E tests

- Add `@axe-core/playwright` to E2E test suite
- Run axe scan on every major page after render
- Fail CI on any WCAG 2.1 AA violation
- Baseline: capture current violations, require zero new ones

### 2. Component-level a11y tests

- Add `vitest-axe` for unit/component tests
- Test each session component: QuestionCard, HintPanel, MathInput, AnswerFeedback
- Test gamification components: XpProgressCard, FlowAmbientBackground
- Test onboarding components: DiagnosticQuiz (when built)

### 3. Custom a11y rules

- Touch target size validator (>= 44x44px)
- Heading hierarchy validator (one h1, no skipped levels)
- `aria-live` presence validator on dynamic content containers
- Math expression `dir="ltr"` validator inside RTL pages
- `prefers-reduced-motion` coverage validator

### 4. CI integration

- a11y tests run on every PR
- Separate CI job: `test:a11y` (does not block other tests)
- Report violations as PR comments with screenshots
- Track a11y violation count over time (should monotonically decrease)

## Files to Create/Modify

- New: `src/student/full-version/tests/a11y/axe-integration.spec.ts`
- New: `src/student/full-version/tests/a11y/component-a11y.spec.ts`
- New: `src/student/full-version/tests/a11y/custom-rules.ts`
- `src/student/full-version/playwright.config.ts` — add a11y test project
- `package.json` — add `@axe-core/playwright`, `vitest-axe` dependencies

## Acceptance Criteria

- [ ] axe-core scans run on all major pages in E2E tests
- [ ] CI fails on new WCAG 2.1 AA violations
- [ ] Component-level a11y tests for all session components
- [ ] Custom rules: touch targets, heading hierarchy, aria-live, math dir, reduced motion
- [ ] a11y test job runs on every PR
- [ ] Violation count tracked over time

> **Dependency**: Should be implemented immediately after RDY-015 (A11y Sweep) to lock in the fixes.
