---
id: FIND-UX-032
task_id: t_6bcdeac9aa0e
severity: P1 — High
lens: ux
tags: [reverify, ux, stub]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-032: Settings/Notifications persists to localStorage only — escalate FIND-ux-016 to P1 (no Phase-1 stubs)

## Summary

Settings/Notifications persists to localStorage only — escalate FIND-ux-016 to P1 (no Phase-1 stubs)

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

## FIND-ux-032: Settings/Notifications persists to localStorage only — wire real backend or hide

**Severity**: p1
**Lens**: ux
**Category**: stub, label-drift
**Related prior finding**: FIND-ux-016 (escalation: was P2 in v1, now P1 because v2 enforces "no Phase-1 stubs")

## Files
- src/student/full-version/src/pages/settings/notifications.vue:19-46
- src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs (add /me/settings if not present)

## Evidence
- Live: clicked Push notifications toggle (default off → on). Network: ZERO POST/PATCH/PUT calls fired against any /api/me/* path.
- File: notifications.vue:19-46 has `// Phase A: local toggles persist to localStorage only` and `function persist() { localStorage.setItem('cena-notification-prefs', ...) }`.
- Cross-device test: incognito window with same user → toggles back to defaults.

## Definition of Done
1. PATCH /api/me/settings is wired and the page calls it on every toggle change.
2. The page reads /api/me/settings on mount and reflects the server state.
3. Optimistic UI on toggle (don't wait for the round-trip).
4. Error handling: if the call fails, the toggle reverts and a snackbar appears.
5. Cross-device test: sign in on two browsers, toggle a pref on browser A, refresh browser B, see the change.
6. **Or**, if the backend cannot land in this sprint: hide the entire page behind a `VITE_ENABLE_SETTINGS_NOTIFICATIONS` flag and surface a "Coming soon — your school admin can manage notifications for now" placeholder.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-032


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_6bcdeac9aa0e`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
