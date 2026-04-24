# TASK-E2E-G-08: GDPR erasure admin trigger (FIND-arch-006)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@gdpr @compliance @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/rtbf-admin.spec.ts`

## Journey

Admin receives DSR → opens `/apps/gdpr/erasure` → enters student id → confirms → `IRightToErasureService` runs → same cascade as E-08 → manifest downloadable.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Action + manifest download |
| Cascade | Same assertions as [TASK-E2E-E-08](TASK-E2E-E-08-right-to-erasure-parent.md) — one code path |
| Audit | Admin operator id recorded |

## Regression this catches

Admin triggers erasure without signed confirmation; manifest missing cascade step; cross-tenant admin triggers on wrong student.

## Done when

- [ ] Spec lands
- [ ] Shares cascade implementation with E-08 (verified identical)
- [ ] Tagged `@gdpr @p0`
