# TASK-E2E-A-01-BE-01: `POST /api/auth/on-first-sign-in` + claims transformer

**Status**: Proposed
**Priority**: P0
**Parent**: [TASK-E2E-A-01](TASK-E2E-A-01-student-register.md)
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @p0 @backend`

## Why this is a prereq

The A-01 spec (`student-register.spec.ts`) asserts that, post-Firebase-signup, the SPA calls `POST /api/auth/on-first-sign-in` and the callback sets `role=student`, `tenant_id`, and `school_id` as Firebase custom claims before redirecting to `/onboarding`. Today that endpoint does not exist (`grep -r "on-first-sign-in" src/` returns nothing). Until it ships, the spec's Firebase + DB boundaries fail on every run.

## What to build

1. `student-api` endpoint `POST /api/auth/on-first-sign-in`
   - Auth: accepts a freshly-issued Firebase idToken (no custom claims yet) in `Authorization: Bearer`
   - Resolves caller's tenant from the registration context (test mode: claim from request body; prod: institute invite token or tenant-assignment policy)
   - Calls Firebase Admin `setCustomUserClaims(uid, { role, tenant_id, school_id })`
   - Creates a `StudentProfile` row under that tenant (idempotent — duplicate POST returns 200, not a second row)
   - Emits `StudentOnboardedV1` (see TASK-E2E-A-01-BE-02) on `cena.events.student.<uid>.onboarded`
2. SPA `register.vue` invokes the endpoint after `registerWithEmail` resolves, then forces an idToken refresh so the new claims land before redirect.
3. Claims transformer unit tests (mock Firebase admin) covering: first-time vs. returning, tenant-collision rejection, missing invite token → 400.

## Boundary tests to unblock

- A-01 boundary #2 (backend contract — `onFirstSignIn` response 2xx)
- A-01 boundary #3 (Firebase — claims present on refreshed JWT)
- A-01 boundary #4a (DB — StudentProfile row with caller's tenant_id)

## Done when

- [ ] Endpoint lives under `src/actors/Cena.Actors/Sso/` (mirror of existing SSO layout)
- [ ] Unit tests for the claims-transformer path (xUnit, mock Firebase admin)
- [ ] SPA register flow calls the endpoint and refreshes idToken pre-redirect
- [ ] A-01 spec's boundaries 2 + 3 + 4a go green on the dev stack
- [ ] Idempotency verified (duplicate POST returns same row, no `StudentOnboardedV1` duplicate)
