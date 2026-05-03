---
id: FIND-QA-008
task_id: t_8387e7d8fecf
severity: P0 — Critical
lens: qa
tags: [reverify, qa, test, admin]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-qa-008: Admin frontend has 1 test file, no E2E — establish baseline suite

## Summary

Admin frontend has 1 test file, no E2E — establish baseline suite

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

Goal: Establish a baseline test suite for the Cena admin web app, which currently has 1 vitest file (25 assertions, 239 lines, no E2E, no Playwright config).

Background:
The admin app under `src/admin/full-version/` has a single vitest file (`tests/user-view.test.ts`) covering only template-tag balance and tab-index lookups for one page. There is NO component test, NO Pinia store test, NO Firebase Auth test, NO router/guard test, NO API client test, and NO E2E test (no Playwright config exists). The admin app is the surface most exposed to tenant-scoping bugs (per FIND-sec-005 / FIND-data-005) and least covered by tests in the entire repo.

Files to read first:
  - src/admin/full-version/tests/user-view.test.ts (the only existing test file)
  - src/admin/full-version/vitest.config.ts (current pattern: tests/**/*.test.ts)
  - src/admin/full-version/package.json (note: scripts.test → vitest run)
  - src/student/full-version/tests/unit/ (40+ exemplar component tests to copy patterns from)
  - src/student/full-version/playwright.config.ts (template for new admin playwright config)
  - .github/workflows/frontend.yml (currently runs `pnpm run test`; needs Playwright job)

Files to touch:
  - src/admin/full-version/tests/unit/ (NEW directory)
  - src/admin/full-version/tests/__mocks__/firebase.ts (NEW — shared Firebase Auth test double)
  - src/admin/full-version/playwright.config.ts (NEW)
  - src/admin/full-version/tests/e2e/admin-smoke.spec.ts (NEW)
  - .github/workflows/frontend.yml (add playwright install + e2e job)

Definition of Done:
  - [ ] At least 10 vitest component tests covering the top admin pages (Question Bank list, Question Bank edit, Users list, Users view, Schools list, Mastery student, Focus Analytics student, AI Settings, FERPA Compliance, Login)
  - [ ] At least 3 Pinia store tests (authStore, schoolStore, questionBankStore)
  - [ ] Shared Firebase Auth mock under `tests/__mocks__/firebase.ts` so no test makes a real network call
  - [ ] Playwright config + 5 smoke E2E specs (login, list users, view user, edit one question, view focus analytics)
  - [ ] Frontend.yml runs `npx playwright install --with-deps chromium` + `pnpm run test:e2e` after the existing `pnpm run test`
  - [ ] vitest coverage on admin > 30% (run `pnpm run test --coverage` and report the number)

Reference: FIND-qa-008 in docs/reviews/agent-qa-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_8387e7d8fecf`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
