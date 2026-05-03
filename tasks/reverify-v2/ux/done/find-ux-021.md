---
id: FIND-UX-021
task_id: t_2a3d67a71b2f
severity: P0 — Critical
lens: ux
tags: [reverify, ux, broken-workflow, stub, i18n]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-021: MSW worker race + raw error message leak — user sees [GET] /api/me 404 Not Found on every cold reload

## Summary

MSW worker race + raw error message leak — user sees [GET] /api/me 404 Not Found on every cold reload

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

## FIND-ux-021: MSW worker race + raw error message leak

**Severity**: p0
**Lens**: ux
**Category**: stub, broken-workflow
**Related prior finding**: none (new)

## Files to read
- src/student/full-version/src/plugins/fake-api/index.ts (worker boot)
- src/student/full-version/src/main.ts (mount order)
- src/student/full-version/src/composables/useApiQuery.ts (error wrapping)
- src/student/full-version/src/pages/home.vue:160-164 (raw error.message render)
- src/student/full-version/src/pages/tutor/index.vue (same — VAlert {{ error.message }})
- src/student/full-version/src/pages/social/index.vue (same)
- src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json (add `common.error*` keys)

## Evidence
- Screenshot: docs/reviews/screenshots/reverify-2026-04-11/ux/04-student-home-mobile-msw-race-error.png — mobile shows `[GET] "/api/me": 404 Not Found` raw error
- Screenshot: docs/reviews/screenshots/reverify-2026-04-11/ux/06-student-home-mobile-hebrew.png — Hebrew RTL, error message NOT translated
- Screenshot: docs/reviews/screenshots/reverify-2026-04-11/ux/08-student-tutor-msw-race-404.png — desktop /tutor with same race
- Console: `[ERROR] Failed to load resource: 404 Not Found @ /api/me, /api/analytics/summary, /api/analytics/time-breakdown, /api/social/class-feed, /api/tutor/threads`

## Goal
1. Cold-load any student route — the user must NEVER see a string containing `[GET]`, `[POST]`, `404 Not Found`, or any quoted URL path.
2. Make MSW boot synchronous-before-mount so the race no longer happens in dev.
3. Make the error-rendering layer translate via i18n keys instead of raw `error.message`.

## Definition of Done
1. Cold-load `/home`, `/tutor`, `/social`, `/settings/notifications`, `/profile` on both 1280×800 and 375×812 viewports — no raw HTTP-error strings visible.
2. Force the backend to error (e.g. shut down MSW handlers manually for the test) — the rendered alert reads "Something went wrong loading your home dashboard. Try again." in EN, an Arabic equivalent in AR, and a Hebrew equivalent in HE. NEVER `[GET] /api/...`.
3. Add Playwright test `tests/e2e/student-msw-race.spec.ts` that loads `/home`, `/tutor`, `/social`, `/settings/notifications` from cold and asserts `expect(page.locator('text=/\[(GET|POST|PUT|DELETE)\] /')).toHaveCount(0)`.
4. Wire it into CI.
5. Add new `common.errorGeneric`, `home.dashboardUnavailable`, `tutor.threadsUnavailable`, `social.feedUnavailable` keys to all three locale files.
6. `useApiQuery` and `useApiMutation` wrap fetch errors in a typed `ApiError` with `code: string` and `i18nKey: string`. Pages render `{{ t(error.i18nKey) }}` with a generic fallback like `t('common.errorGeneric')`.

## Reporting requirements
Branch: `<worker>/find-ux-021-msw-race-error-leak`
In `--result`, paste the network log + a screenshot of the same `/home` cold load showing the polite i18n'd error.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-021


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_2a3d67a71b2f`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
