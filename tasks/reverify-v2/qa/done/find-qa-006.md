---
id: FIND-QA-006
task_id: t_d2688b9bc6fb
severity: P1 — High
lens: qa
tags: [reverify, qa, test, regression, ux]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-qa-006: ux-008/009/010/013 regression tests missing — brand/auth/leaderboard

## Summary

ux-008/009/010/013 regression tests missing — brand/auth/leaderboard

## Severity

**P1 — High** — REGRESSION

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

Goal: Add component-level regression tests that lock down the brand title rewrite, leaderboard "you" row, and mock-auth hard-nav rehydration.

Background:
FIND-ux-008/009/010/013 were closed by commit 89e0a93 / 0a39231. The bundle modified four existing vitest specs to clear localStorage in beforeEach but added no assertion that proves the four user-visible bugs are gone. Specifically:
  - The brand title rewrite (per-page document.title via router afterEach) is not tested.
  - The leaderboard "you" row no longer being hardcoded as "Dev Student (You)" is not asserted.
  - The mock-auth surviving a hard navigation (rehydration from localStorage) is asserted as a side effect of beforeEach cleanup but not as the primary contract.

Files to read first:
  - src/student/full-version/src/plugins/1.router/guards.ts (router.afterEach hook that rewrites document.title)
  - src/student/full-version/src/plugins/fake-api/mockSession.ts (added in 89e0a93 — the centralised mock-session helper)
  - src/student/full-version/src/stores/authStore.ts (rehydration logic)
  - src/student/full-version/src/stores/meStore.ts
  - src/student/full-version/tests/unit/authStore.spec.ts (existing — extend)
  - src/student/full-version/tests/unit/LeaderboardPreview.spec.ts (existing — extend)

Files to touch:
  - src/student/full-version/tests/unit/brandTitle.spec.ts (NEW)
  - src/student/full-version/tests/unit/LeaderboardPreview.spec.ts (extend)
  - src/student/full-version/tests/unit/authStore.spec.ts (add hard-nav rehydration test)

Definition of Done:
  - [ ] brandTitle.spec.ts mounts the layout / nav, simulates a route push, and asserts document.title is rewritten per route per the themeConfig.brandTitle pattern
  - [ ] LeaderboardPreview test asserts the "you" row resolves from mockSession (not hardcoded)
  - [ ] authStore test simulates a hard navigation: reset Pinia, re-init store, assert mock user is rehydrated from localStorage `cena-mock-auth`
  - [ ] Each test fails on a targeted synthetic regression of its bug
  - [ ] All tests run as part of `npm run test:unit` and the existing student-web-ci.yml workflow

Reference: FIND-qa-006 in docs/reviews/agent-qa-reverify-2026-04-11.md
Related prior findings: FIND-ux-008, FIND-ux-009, FIND-ux-010, FIND-ux-013


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_d2688b9bc6fb`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
