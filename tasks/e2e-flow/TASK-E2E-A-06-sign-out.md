# TASK-E2E-A-06: Sign-out clears all surfaces

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/sign-out.spec.ts`

## Journey

Signed-in → click sign-out → localStorage cleared + httpOnly cookie expired + SignalR disconnected + Firebase token revoked → subsequent `/api/me/profile` returns 401.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Redirect to `/login`; no residual UI from prior session |
| localStorage | Auth keys removed |
| Cookie | Session cookie `max-age=0` / expired |
| SignalR | Hub connection closed, not reconnecting |
| Backend | `/api/me/*` returns 401 |

## Regression this catches

Zombie session (backend still trusts old token after client signs out); SignalR reconnects with stale token; residual tenant-id in localStorage.

## Done when

- [ ] Spec lands
- [ ] Multi-tab scenario: sign-out in one tab propagates to other tabs
