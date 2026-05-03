# TASK-E2E-A-03: Password reset

**Status**: Spec landed at `tests/e2e-flow/workflows/password-reset.spec.ts`. 2 tests listed: full reset via emu OOB + no-enumeration on unknown email.
**Priority**: P1
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/password-reset.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

`/forgot-password` → email entered → Firebase emu queues reset email → test fetches reset link from Firebase emulator OOB endpoint → new password set → sign in with new password succeeds; old password rejected.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Success message shown after submit; sign-in with new password succeeds |
| Firebase emu OOB | Reset code retrievable; single-use |
| API | Sign-in with old password → 401; with new password → 200 |

## Regression this catches

Reset link reusable; old password still honored after reset; reset link cross-tenant redirect.

## Done when

- [ ] Spec lands
- [ ] Single-use token asserted (second use → 409)
- [ ] Runs < 30s
