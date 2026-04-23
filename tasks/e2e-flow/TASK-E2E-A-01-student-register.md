# TASK-E2E-A-01: Student registration → Firebase → tenant → home

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/student-register.spec.ts`

## Journey

`/register` → email + password → Firebase emu creates account → student-api POST `/api/auth/on-first-sign-in` → custom claims set (`role=student`, `tenant_id`, `school_id`) → SPA redirects to `/onboarding` (first-time) or `/home` (returning).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | `/onboarding` rendered within 10s post-submit; no login re-prompt |
| DB | `StudentProfile` row created with caller's `tenant_id` |
| Bus | `StudentOnboardedV1` on `cena.events.student.*.onboarded` within 5s |
| Firebase | JWT contains `role=student`, `tenant_id`, `school_id` custom claims |

## Regression this catches

Missing claims → 401 loop; wrong tenant → cross-institute visibility; duplicate StudentProfile on retry.

## Done when

- [ ] Spec at the path above
- [ ] All 4 boundaries asserted
- [ ] Runs < 45s
- [ ] Tagged `@auth @p0`
