---
id: FIND-UX-025
task_id: t_da7affaf863c
severity: P1 — High
lens: ux
tags: [reverify, ux, a11y, wcag2.2]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-025: All 4 student header icon buttons missing aria-label — sidebar toggle, language, theme, notifications

## Summary

All 4 student header icon buttons missing aria-label — sidebar toggle, language, theme, notifications

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

## FIND-ux-025: All 4 student header icon buttons missing aria-label

**Severity**: p1
**Lens**: ux
**Category**: a11y, WCAG-2.2-AA
**Related prior finding**: FIND-ux-017 (notifications "Badge" — partial overlap)

## Files
- src/student/full-version/src/layouts/components/DefaultLayoutWithVerticalNav.vue:42 (sidebar toggle)
- src/student/full-version/src/@core/components/I18n.vue (language switcher activator)
- src/student/full-version/src/layouts/components/NavbarThemeSwitcher.vue (theme toggle)
- src/student/full-version/src/layouts/components/NavBarNotifications.vue (notifications activator)
- src/student/full-version/src/plugins/i18n/locales/en.json + ar.json + he.json (4 new keys)

## Evidence
- DOM probe: 4 header buttons in `.layout-navbar` with `tabler-menu-2`, `tabler-language`, `tabler-device-desktop-analytics`, `tabler-bell` icons — all with `ariaLabel: null`.
- axe-core: `button-name` critical, count=3 (the 4th hides on routes that gate the language switcher).

## Definition of Done
1. Each of the 4 IconBtn calls receives an `:aria-label="t('nav.<key>')"`.
2. New keys: `nav.toggleSidebar`, `nav.languageSwitcher`, `nav.toggleTheme`, `nav.notificationsBell` — added to all 3 locale files.
3. axe-core scan of /home, /tutor, /social, /progress reports zero `button-name` violations on header buttons.
4. Add `tests/e2e/a11y-header-button-names.spec.ts` per test_required.

## Coordination
Land together with FIND-ux-022 and FIND-ux-028 as one a11y header pass.

## Reporting requirements
Branch: `<worker>/find-ux-025-header-button-names`
In `--result`, paste before/after axe `button-name` count.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-025


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_da7affaf863c`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
