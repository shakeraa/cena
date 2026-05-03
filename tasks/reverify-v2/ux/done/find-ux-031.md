---
id: FIND-UX-031
task_id: t_169f384cdae3
severity: P1 — High
lens: ux
tags: [reverify, ux, stub, auth]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-031: OAuth provider buttons fake the sign-in — bypass real Google/Apple/Microsoft/phone

## Summary

OAuth provider buttons fake the sign-in — bypass real Google/Apple/Microsoft/phone

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

## FIND-ux-031: OAuth provider buttons fake the sign-in — bypass real Google/Apple/Microsoft/phone

**Severity**: p1
**Lens**: ux
**Category**: stub, label-drift
**Related prior finding**: FIND-ux-023 (same root: stub auth)

## Files
- src/student/full-version/src/components/common/AuthProviderButtons.vue:55-70

## Evidence
- Live: clicked "Continue with Google" → page navigates to /home with `Hi Google User` greeting; localStorage["cena-mock-auth"] gets `{"uid":"mock-google-mnuqx38i","email":"google-user@example.com","displayName":"Google User"}`.
- No Firebase popup, no redirect, no Google.
- File: AuthProviderButtons.vue:62 — `authStore.__mockSignIn({ uid, email: fakeEmail, displayName })` with hard-coded displayName.
- All four provider buttons (Google, Apple, Microsoft, phone) hit the same __mockSignIn path.

## Definition of Done
See FIND-ux-023 task body. This is the OAuth half of the same fix:
1. Replace each provider button's handler with a real `signInWithPopup(firebaseAuth, ...)` call.
2. Delete the 'Continue with phone' button if no phone-auth backend exists yet.
3. Delete the 'Continue with Microsoft' button if no Microsoft provider is configured.
4. Test that clicking "Continue with Google" actually opens the Google OAuth popup against `cena-platform`.

## Coordination
Schedule together with FIND-ux-023 in one student-auth task.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-031


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_169f384cdae3`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
