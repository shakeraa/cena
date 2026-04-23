# TASK-E2E-A-05: Role-claim cache invalidation

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @rbac @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/role-claim-invalidation.spec.ts`

## Journey

Admin promotes a student to teacher role via admin console → student's next SPA request reflects teacher-only UI within < 2 min (idToken refresh cycle).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Firebase | Custom claims updated server-side |
| DOM | Student sees teacher-only UI (`/apps/teacher/heatmap` navigable) |
| idToken | New idToken contains updated `role` claim |
| Backend | `/api/teacher/*` accepts old-token requests only until refresh |

## Regression this catches

60-second stale-claim hole (RDY-056 precedent); role downgrade not taking effect; forced sign-out not clearing cached claims.

## Done when

- [ ] Spec lands
- [ ] Both promotion and demotion paths tested
- [ ] Tagged `@rbac @p0`
