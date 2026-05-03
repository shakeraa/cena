---
id: FIND-UX-024
task_id: t_47762c224fe9
severity: P0 — Critical
lens: ux
tags: [reverify, ux, stub, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-ux-024: Class-feed reaction button silently fails AND increments to fixed value 1 (regression of FIND-ux-011)

## Summary

Class-feed reaction button silently fails AND increments to fixed value 1 (regression of FIND-ux-011)

## Severity

**P0 — Critical** — REGRESSION

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

## FIND-ux-024: Class-feed reaction button silently fails AND increments to fixed value 1

**Severity**: p0
**Lens**: ux
**Category**: stub, regression
**Related prior finding**: FIND-ux-011 (partially-fixed: friends + peers patched, social class-feed missed)

## Files
- src/student/full-version/src/pages/social/index.vue:25-77 (handler swallow + missing error surface)
- src/student/full-version/src/plugins/fake-api/handlers/student-social/index.ts:101-110 (hard-coded newCount: 1)
- src/student/full-version/src/components/social/ClassFeedItemCard.vue (a11y label on heart button)

## Evidence
- File: pages/social/index.vue:25-33 — handleReact catches and discards errors with comment "error surfaced via reactMutation.error", but the template at lines 56-77 only renders `feedQuery.error.value`, NEVER `reactMutation.error`. Same swallow-and-smile as FIND-ux-011.
- File: handlers/student-social/index.ts:101-110 — POST /api/social/reactions returns hard-coded `newCount: 1` regardless of input.
- Live test: clicked heart button, count stayed at 12 (because feedQuery.refresh() returns the same hard-coded class-feed list).

## Goal
User clicks heart → count goes up by 1. User clicks heart again → count goes back down. If the call fails, an error toast appears. The button's accessible name is "Like (12)" not just "12".

## Definition of Done
1. The MSW handler holds per-item reaction state in a Map and returns the updated count.
2. `social/index.vue` template renders `<VSnackbar>` or `<VAlert>` bound to `reactMutation.error.value`. The catch block no longer just swallows.
3. The heart and comment buttons have `:aria-label="t('social.feed.reactionAriaLabel', { count: item.reactionCount })"` so screen-reader users hear "Like, 12 reactions".
4. Add `tests/e2e/student-social-react.spec.ts` that clicks the heart twice and asserts the count goes 12 → 13 → 12.
5. Add the same test against the friends-accept and peer-vote flows from the original FIND-ux-011 to make sure those error toasts still surface.

## Reporting requirements
Branch: `<worker>/find-ux-024-class-feed-react`
In `--result`, paste a Playwright trace showing the count incrementing.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-024


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_47762c224fe9`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
