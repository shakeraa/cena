# TASK-E2E-H-02: Admin A cannot query admin B's users

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @rbac @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cross-tenant-admin-users.spec.ts`

## Journey

Institute admin for A hits `/api/admin/users` → response contains A's users only → direct-id probe `/api/admin/users/{b-user-id}` → 404 (not 403 — don't leak existence).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Listing | Contains A only; ids from B absent |
| Id probe | Returns 404 not 403 |

## Regression this catches

403 leaks existence of other institute's user; admin enumeration by id.

## Done when

- [ ] Spec lands
