# TASK-E2E-A-02: Sign-in path (existing account)

**Status**: Spec landed at `tests/e2e-flow/workflows/sign-in.spec.ts` (note: filename `sign-in.spec.ts`). 2 tests listed: happy path + wrong-password rejection.
**Priority**: P0
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/sign-in.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

`/login` → email + password → Firebase issues idToken → SPA stores token → fetches `/api/me/profile` → lands on `/home`.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | `/home` rendered; no login re-prompt after refresh |
| API | `/api/me/profile` returns caller's own tenant, not another's |
| Cookie (prod path) | httpOnly session cookie set per ADR-0046 |
| Firebase | idToken stored + refresh flow works |

## Regression this catches

Stale-token acceptance; wrong-tenant profile served; cookie-scope leak across subdomains.

## Done when

- [ ] Spec lands
- [ ] All boundaries asserted
- [ ] Runs < 30s
- [ ] Tagged `@auth @p0`
