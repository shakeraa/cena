# TASK-E2E-H-07: Firebase claim tenant matches backend tenant

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @auth @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/firebase-tenant-match.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

User authenticates → backend reads `tenant_id` from JWT custom claim → every write tags that tenant — never a query-param, never a request-body value.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Backend | Refuses writes where path `{tenantId}` differs from JWT claim |
| Audit | Mismatch attempt logged |

## Regression this catches

URL-tampering allows tenant override; JWT-less write path; tenant-from-body (attacker-controlled).

## Done when

- [ ] Spec lands
- [ ] Malicious payload (forged tenant in body) → 403 asserted
