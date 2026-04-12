---
id: FIND-UX-020
task_id: t_bcba38da3393
severity: P0 — Critical
lens: ux
tags: [reverify, ux, broken-workflow, casl]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-020: Student desktop sidebar is empty — CASL gate filters every nav item because userAbilityRules cookie never set

## Summary

Student desktop sidebar is empty — CASL gate filters every nav item because userAbilityRules cookie never set

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

## FIND-ux-020: Student desktop sidebar is empty — wire CASL or remove the gate

**Severity**: p0
**Lens**: ux
**Category**: ux, broken-workflow
**Related prior finding**: none

## Files to read
- src/student/full-version/src/@layouts/components/VerticalNavLink.vue:22
- src/student/full-version/src/@layouts/components/VerticalNavSectionTitle.vue:18
- src/student/full-version/src/@layouts/plugins/casl.ts:15-25
- src/student/full-version/src/plugins/casl/index.ts:8-13
- src/student/full-version/src/stores/authStore.ts:113-120
- src/student/full-version/src/navigation/vertical/index.ts (the 14 nav items that never render)
- src/admin/full-version/src/composables/useFirebaseAuth.ts:79 (the admin path that DOES set userAbilityRules)

## Evidence
- Screenshot: docs/reviews/screenshots/reverify-2026-04-11/ux/09-student-empty-sidebar-desktop.png
- DOM probe: `aside.layout-vertical-nav ul.nav-items` contains 20 `<!--v-if-->` placeholders, ZERO `<li>` children.
- Cookie probe: `document.cookie` after sign-in contains NO `userAbilityRules` cookie.
- Root cause: `v-if="can(item.action, item.subject)"` with empty MongoAbility resolves to false.

## Goal
After signing in (mock or real Firebase) on desktop ≥1280px width, the student vertical sidebar must render all 14 nav items + 5 section headings.

## Pick one fix path (decide in code review)
1. **Drop CASL on student**: remove `v-if="can(item.action, item.subject)"` from VerticalNavLink.vue and VerticalNavSectionTitle.vue for the student build, OR fork those components into student-specific copies. Justification: student app has no role-based menu hiding; every signed-in student sees the same nav.
2. **Seed abilities on sign-in**: in `authStore.__mockSignIn` and (when wired) the real Firebase path, write `useCookie('userAbilityRules').value = [{ action: 'manage', subject: 'all' }]` and `ability.update([...])`. Mirror the admin path at `src/admin/full-version/src/composables/useFirebaseAuth.ts:79`.

## Definition of Done
1. Desktop view of `/home`, `/tutor`, `/progress`, `/settings/notifications` shows the full sidebar with at least 14 nav links + section headings.
2. Sign-out clears the abilities so a fresh visit to /login does NOT show stale nav.
3. Add `tests/e2e/student-sidebar-nav.spec.ts` that signs in via the existing E2E mock helper and asserts `await page.locator('aside.layout-vertical-nav .nav-items > li').count() >= 14`.
4. axe-core no longer reports `list` violation on the sidebar `<ul>` — currently the empty `<ul>` has `<div>` children which axe flags as `Fix all of the following: List element has direct children that are not allowed: div` (see student-home-mobile.json). Note this is also tracked separately as FIND-ux-037.

## Reporting requirements
Branch: `<worker>/find-ux-020-student-sidebar`
In `--result`, paste before/after `await page.locator('aside.layout-vertical-nav .nav-items > li').count()` from a manual Playwright session.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-020


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_bcba38da3393`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
