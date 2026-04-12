---
id: FIND-UX-019
task_id: t_89e7d3c33286
severity: P0 — Critical
lens: ux
tags: [reverify, ux, a11y, contrast, wcag2.2]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-019: Vuexy primary #7367F0 fails WCAG 2.2 AA contrast — fix via usage pattern (4.26:1 light, 2.91:1 dark)

## Summary

Vuexy primary #7367F0 fails WCAG 2.2 AA contrast — fix via usage pattern (4.26:1 light, 2.91:1 dark)

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

## FIND-ux-019: Fix Vuexy primary color (#7367F0) WCAG 2.2 AA contrast violations via usage-pattern-only changes

**Severity**: p0
**Lens**: ux
**Category**: a11y, WCAG-2.2-AA
**Related prior finding**: none (new — color is locked but usage pattern was never measured)

## Scope
WCAG 2.2 AA. Affected: every page that uses `class="text-primary"` for body-size text, and `bg-primary` buttons with small white text.

## HARD CONSTRAINT
`#7367F0` is the LOCKED Vuexy primary. Do NOT propose a palette change. Fix via usage pattern only.

## Files to read
- src/admin/full-version/src/pages/login.vue:210-215 (Forgot Password? link)
- src/student/full-version/src/components/auth/StudentAuthCard.vue (footer slot links)
- src/student/full-version/src/pages/login.vue (forgot + register footer links)
- All other call sites (`grep -rn 'class="text-primary"' src/{admin,student}/full-version/src --include='*.vue'`)

## Evidence
- Lighthouse JSON: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/admin-login.json
- Lighthouse JSON: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/student-login.json
- Computed contrast: `#7367F0` on `#FFFFFF` = 4.26:1 (AA-fail), on `#2f3349` = 2.91:1 (AA-fail).

## Definition of Done
1. axe-core run (in headless Chrome via Lighthouse OR via vitest + jsdom + axe) on admin /login, student /login, student /home, student /forgot-password, student /register reports zero `color-contrast` violations.
2. Lighthouse a11y score for those 5 pages ≥ 95 (currently 84/90/90/91/90).
3. Each fixed call-site uses ONE of these patterns (NOT the original `text-primary` text affordance):
   - Wrapped in `<VBtn variant="text" color="primary">` (button affordance, tonal background lifts contrast above 4.5)
   - Replaced with `text-default` or `text-medium-emphasis` (no decorative primary on body text)
   - Promoted to ≥14pt bold OR ≥18pt regular (large-text rule: 3:1)
   - Dark-theme alternate color `#a59cf5` for those specific text nodes
4. Add a vitest test `tests/a11y/color-contrast.spec.ts` that boots both apps in jsdom, navigates to each page above, runs axe-core, and asserts zero `color-contrast` violations.
5. **DO NOT** change `themeConfig.primary` or any reference to `#7367F0`.

## Reporting requirements
Branch name: `<worker>/find-ux-019-contrast-fix`
In your `--result` include before/after Lighthouse a11y scores for the 5 pages.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-019


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_89e7d3c33286`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
