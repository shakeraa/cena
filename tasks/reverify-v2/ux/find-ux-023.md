---
id: FIND-UX-023
task_id: t_9b19a52d72df
severity: P0 — Critical
lens: ux
tags: [reverify, ux, stub, auth, firebase]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-023: Student login is a stub — wire real Firebase Auth (cena-platform), delete __mockSignIn

## Summary

Student login is a stub — wire real Firebase Auth (cena-platform), delete __mockSignIn

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

## FIND-ux-023: Student login is a stub — wire real Firebase Auth (cena-platform)

**Severity**: p0
**Lens**: ux
**Category**: stub, ux
**Related prior finding**: FIND-ux-006 (forgot-password silent drop — same anti-pattern, different surface)

## Files to read
- src/student/full-version/src/pages/login.vue:42-109 (stub login flow)
- src/student/full-version/src/stores/authStore.ts (delete __mockSignIn after wire-up)
- src/student/full-version/src/plugins/firebase.ts (replace stub plugin with real Firebase init)
- src/student/full-version/src/components/common/AuthProviderButtons.vue (Google/Apple/MS/phone — also call real Firebase)
- src/admin/full-version/src/composables/useFirebaseAuth.ts (mirror this pattern, minus the ADMIN_ROLES gate)
- src/student/full-version/.env.example (add VITE_FIREBASE_* keys or note emulator usage)

## Evidence
- File: src/student/full-version/src/pages/login.vue:71-101 — `if (payload.email === 'fail@test.com')` then `__mockSignIn(...)` for any other email.
- Interaction: filled email=anything@anywhere, password=anything, clicked Sign in → no `/api/auth/login` call, no Firebase, page navigated to /home.
- Comment in code: "Mock-backend rules for Phase A".

## Goal
Student sign-in goes through real Firebase Auth against `cena-platform`. Wrong password shows an error. Good password sets a real ID token. The MSW worker still mocks the BACKEND `/api/me` etc., but Firebase Auth itself is real.

## Definition of Done
1. Student login.vue calls `signInWithEmailAndPassword(firebaseAuth, ...)`. Wrong creds show real Firebase error code translated to user-friendly i18n key.
2. Successful sign-in stores a real ID token in `authStore.idToken` (not a `mock-token-{uid}` string).
3. `__mockSignIn` function and the `if (payload.email === 'fail@test.com')` branch are DELETED.
4. AuthProviderButtons.vue's Google/Apple buttons call real `signInWithPopup(firebaseAuth, GoogleAuthProvider)` etc. The Microsoft + Phone buttons either get wired or are removed (both currently bypass to /home with hard-coded "Google User" / "Apple User" / "Microsoft User" displayName — see FIND-ux-031).
5. Local dev uses Firebase emulator: add `npm run firebase:emulators` and document in README. Tests run against the emulator.
6. The 5 OAuth provider buttons either work or are removed.

## Constraint
This shares scope with FIND-ux-006c (forgot-password backend wire-up) and FIND-ux-021 (the MSW race) and FIND-ux-031 (the OAuth-button stubs). Coordinate with the queue coordinator to schedule them in one student-auth block.

## Reporting requirements
Branch: `<worker>/find-ux-023-real-firebase-auth`
In `--result`, paste the network trace from a successful sign-in showing real `identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=...` call.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-023


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_9b19a52d72df`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
