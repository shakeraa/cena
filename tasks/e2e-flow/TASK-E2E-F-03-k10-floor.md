# TASK-E2E-F-03: Classroom analytics k=10 floor (prr-026)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-F](EPIC-E2E-F-teacher-classroom.md)
**Tag**: `@privacy @k-floor @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/classroom-k10-floor.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Teacher → `/apps/classroom/analytics` → stats shown only when classroom has ≥10 active students → below floor: privacy message, no numbers.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | <10 → privacy message; ≥10 → stats |
| Backend | Hit `/api/teacher/analytics` with 9 students → empty body; with 10 → populated |
| Negative-property | Frontend-only bypass impossible |

## Regression this catches

k-floor enforced only on frontend (bypassable); teacher with 5 students enumerates individual mastery.

## Done when

- [ ] Spec lands
- [ ] Tagged `@k-floor @p0`
