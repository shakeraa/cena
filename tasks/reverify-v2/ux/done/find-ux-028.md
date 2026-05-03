---
id: FIND-UX-028
task_id: t_e74d3e4c9ee0
severity: P1 — High
lens: ux
tags: [reverify, ux, i18n, a11y, label-drift]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-028: Notifications bell labeled תג (Hebrew Badge default) — escalate FIND-ux-017 to P1

## Summary

Notifications bell labeled תג (Hebrew Badge default) — escalate FIND-ux-017 to P1

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

## FIND-ux-028: Notifications bell labeled "תג" (Hebrew Badge default) — escalate FIND-ux-017 to P1

**Severity**: p1
**Lens**: ux
**Category**: i18n, label-drift, WCAG-2.2-AA
**Related prior finding**: FIND-ux-017 (Notifications "Badge" — escalation)

## Files
- src/student/full-version/src/layouts/components/NavBarNotifications.vue
- locale files (add `nav.notificationsBell` if not present)

## Evidence
- Snapshot at /session with cena-student-language=he: notifications button labeled `"תג"` (Hebrew word for "tag/badge"). Vuetify VBadge default name passes through i18n.

## Definition of Done
1. Notifications bell button has explicit `aria-label="t('nav.notificationsBell')"`.
2. The VBadge inside has `aria-label=""` (empty) to suppress the Vuetify default.
3. Tested in EN ("Notifications"), AR ("إشعارات"), HE ("התראות").
4. Same pattern applied to the user-profile-avatar's badge (the green online dot).

## Coordination
Land together with FIND-ux-022 and FIND-ux-025 as one a11y header pass.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-028


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_e74d3e4c9ee0`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
