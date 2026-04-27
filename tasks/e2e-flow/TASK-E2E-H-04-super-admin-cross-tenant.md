# TASK-E2E-H-04: SUPER_ADMIN can cross tenants; ADMIN cannot

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @rbac @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/super-admin-cross-tenant.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

SUPER_ADMIN hits `/api/admin/users?tenant=X` → sees X's data → ADMIN pinned to own tenant hits same with `?tenant=X` → response filtered to own tenant (query param ignored).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| RBAC | `?tenant=X` honored for SUPER_ADMIN; ignored for ADMIN |
| Audit | SUPER_ADMIN cross-tenant reads logged |

## Regression this catches

Query param honored for ADMIN (privilege escalation); SUPER_ADMIN reads un-audited.

## Done when

- [ ] Spec lands
