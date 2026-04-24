---
id: FIND-UX-033
task_id: t_43a02f11c2b4
severity: P1 — High
lens: ux
tags: [reverify, ux, label-drift, i18n]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-033: Admin login labels are title-case + not i18n'd — sentence-case + extract

## Summary

Admin login labels are title-case + not i18n'd — sentence-case + extract

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

## FIND-ux-033: Admin login labels are title-case — sentence-case + i18n them

**Severity**: p1
**Lens**: ux
**Category**: ux, label-drift

## Files
- src/admin/full-version/src/pages/login.vue (button labels at 213, 224, 248, 263)
- src/admin/full-version/src/plugins/i18n/locales/*.json (add `auth.signIn`, `auth.signInWithGoogle`, etc.)
- Same for register.vue and forgot-password.vue

## Evidence
- Screenshot: docs/reviews/screenshots/reverify-2026-04-11/ux/07-admin-login.png shows "Sign In", "Sign In With Google", "Sign In With Apple", "Forgot Password?".
- File: login.vue:223-225 has hard-coded `<VBtn>Sign In</VBtn>`.

## Definition of Done
1. All 4 OAuth/email buttons + "Forgot password?" link are i18n keys.
2. EN values use sentence-case ("Sign in", "Sign in with Google", "Forgot password?").
3. AR + HE locale values added.
4. Snapshot test asserts the rendered text matches the i18n key values exactly.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-033


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_43a02f11c2b4`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
