# REPORT-EPIC-A — Auth & Onboarding (real-browser journey)

**Status**: ✅ green — 13 passed / 1 fixme (intentional, see below) / 0 failed
**Date**: 2026-04-27
**Worker**: claude-1
**Spec dir**: `src/student/full-version/tests/e2e-flow/workflows/`
**Reference**: `student-full-journey.spec.ts` (already covers A-01 + B happy path)

## Specs run

| Task   | Spec file | Tests | Result |
|--------|-----------|-------|--------|
| A-01   | `student-register.spec.ts` | 1 | ✅ |
| A-02   | `sign-in.spec.ts` | 2 | ✅ |
| A-03   | `password-reset.spec.ts` | 2 | ✅ |
| A-04   | `parent-child-binding.spec.ts` | 3 | ✅ |
| A-05   | `role-claim-invalidation.spec.ts` | 3 + 1 fixme | ✅ |
| A-06   | `sign-out.spec.ts` | 2 | ✅ |

The fixme on A-05 (full SUPER_ADMIN → claims-refresh < 2min flow) is intentional — `AdminRoleService.AssignRoleToUserAsync` requires an `AdminUser` Marten doc keyed by Firebase uid, and `/api/admin/me` only auto-bootstraps that doc for ADMIN-or-above callers. A fresh STUDENT user therefore can't be promoted today without a precursor admin-side bootstrap. Header comments in `role-claim-invalidation.spec.ts` describe the exact unblock path.

## UI buttons clicked

- `/register`: age-gate-dob input, age-gate-next, auth-display-name, auth-email, auth-password, auth-submit
- `/login`: auth-email, auth-password, auth-submit
- `/forgot-password`: forgot-email, forgot-submit
- Default layout: user-profile-avatar-button, user-profile-signout

## API endpoints fired

- `POST /api/auth/on-first-sign-in` (A-01 + register flow)
- `POST /api/auth/password-reset` (A-03)
- `GET /api/me` (A-02 token check; new — added by hydration fix)
- `POST /api/me/onboarding` (A-06 beforeAll)
- `POST /api/parent/bind/{token}` (A-04)
- `GET /api/parent/dashboard` (A-04)
- `POST /api/admin/users/{id}/role` (A-05)

## Bus events observed

A-01 path emits `student-onboarded` via the existing flow. The remaining EPIC-A specs assert at the API/DOM boundary; the bus side is covered by their backend siblings (`TASK-E2E-A-01-BE-02-student-onboarded-event.md` etc).

## Production-grade bugs fixed (no stubs)

The previous run produced 4 failures (A-03 × 2 and A-06 × 2). All four root-caused to real production bugs — none were spec adjustments.

### 1. Password-reset endpoint returned 503 in dev — Firebase Admin SDK never initializes locally
- **File**: `src/shared/Cena.Infrastructure/Firebase/FirebaseAdminService.cs`
- **Symptom**: `POST /api/auth/password-reset` returned 503; the SPA's `/forgot-password` page surfaced "Service temporarily unavailable" instead of the success card.
- **Root cause**: `GeneratePasswordResetLinkAsync` early-returned `FirebaseUnavailable` whenever `_initialized == false`. The dev stack has no GCP credentials by design, so initialization always fails and password-reset was effectively dead in dev.
- **Fix**: Added a `GeneratePasswordResetViaEmulatorAsync` fallback that posts directly to the Identity-Toolkit emulator's `accounts:sendOobCode` endpoint when `FIREBASE_AUTH_EMULATOR_HOST` is set. Mirrors the existing `SetCustomClaimsViaEmulatorAsync` pattern. Production unaffected — only fires when the emulator-host env var is present. OWASP enumeration parity preserved (EMAIL_NOT_FOUND → `UserNotFound`; caller still maps both LinkGenerated and UserNotFound to 204).

### 2. Every signed-in user bounced to /onboarding even when fully onboarded
- **File**: `src/student/full-version/src/plugins/firebase.ts`
- **Symptom**: After `/login`, the SPA route guard saw `meStore.profile == null`, treated the user as not-onboarded, and redirected `/home → /onboarding` regardless of the projection's `onboardedAt` field.
- **Root cause**: `onAuthStateChanged` only updated `authStore` and CASL abilities. The promised `STU-W-03 will wire the real /api/me fetch on login` (per the meStore comment) never landed — meStore stayed null after Firebase sign-in.
- **Fix**: Added `hydrateMeFromApi(idToken, firebaseUser, meStore)` inside the auth-state handler. 200 → `__setProfile(real data)`. 404 → `__setProfile({uid, …, onboardedAt: null})` so the user correctly lands on /onboarding. 401/5xx → leave previous profile in place (a separate page will surface the error).

### 3. Login.vue raced /api/me hydration with router.replace
- **File**: `src/student/full-version/src/pages/login.vue`
- **Symptom**: Even with the fix above, the first navigation after sign-in ran the route guard *before* `/api/me` resolved, so `meStore.profile` was still null.
- **Root cause**: `handleFirebaseSubmit` waited 50ms after `loginWithEmail` then called `navigateAfterLogin()`. 50ms isn't enough headroom for the `onAuthStateChanged` listener + `/api/me` round-trip.
- **Fix**: Replaced the fixed timeout with a `watch` on `meStore.profile` that resolves when the profile is set. 5-second hard cap so a backend hiccup can't strand the user on `/login`.

### 4. UserProfile.signOut never called firebase.signOut() — IndexedDB session re-hydrated user on next nav
- **File**: `src/student/full-version/src/layouts/components/UserProfile.vue`
- **Symptom**: After clicking "Sign out", `page.goto('/home')` redirected to `/onboarding` instead of `/login`. Sign-out only cleared local stores; Firebase still considered the user signed in.
- **Root cause**: The handler called `authStore.__signOut()` and `meStore.__setProfile(null)` but never invoked Firebase JS SDK's `signOut(auth)`. The SDK's IndexedDB-persisted session survived, and the next page-load fired `onAuthStateChanged` again with the same user — re-hydrating authStore via `__firebaseSignIn`.
- **Fix**: Now calls `firebaseSignOut(getFirebaseAuth())` before clearing local stores. Mock-auth path (`useMockAuth=true`) skips the Firebase call as before.

## Diagnostics summary (final live run)

- **Tests passed**: 13 (+1 deliberately fixme)
- **Failed**: 0
- **Console errors / page errors / unexpected 4xx-5xx**: not separately collected for these spec files (they assert at the API/DOM boundary). The diagnostic-collection pattern is captured in `student-full-journey.spec.ts` and will be the template for EPIC-C onwards where there's no pre-existing assertion-style spec.
- **Container logs**: no Marten-Sessions noise; `Firebase Admin SDK not initialized` warnings during dev startup are expected (the emulator-fallback path now compensates).

## Build gate

```
$ dotnet build src/actors/Cena.Actors.sln --nologo --verbosity minimal
83 Warning(s)
0 Error(s)
Time Elapsed 00:00:28.80
```

All 83 warnings are pre-existing (xUnit analyzer rules, obsolete API usage in tests, etc.). None introduced by these fixes.

## What's next

EPIC-B already has a green happy-path spec (`subscription-happy-path.spec.ts`). Extensions queued: B-02 declined, B-03 cancel-back, B-04 tier-upgrade, B-06 cancel-at-period-end, B-08 bank transfer.
