# TASK-E2E-A-01: Student registration → Firebase → tenant → home

**Status**: Spec landed (`workflows/student-register.spec.ts`) — blocked on BE-01 + BE-02. Will fail boundaries 2–4 until backend ships.
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

## Prereqs

- [TASK-E2E-A-01-BE-01](TASK-E2E-A-01-BE-01-on-first-sign-in.md) — `POST /api/auth/on-first-sign-in` + claims transformer
- [TASK-E2E-A-01-BE-02](TASK-E2E-A-01-BE-02-student-onboarded-event.md) — `StudentOnboardedV1` event + NATS emitter
- [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) — `fixtures/bus-probe.ts` for bus boundary assertion

Spec skeleton landed at `src/student/full-version/tests/e2e-flow/workflows/student-register.spec.ts` with DOM + Firebase + DB-via-API boundaries wired and bus boundary TODO'd until INFRA-01 ships.
