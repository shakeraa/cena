# TASK-E2E-G-03: Reference-calibrated recreation (RDY-019b / ADR-0043)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @content @ship-gate @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/reference-recreation.spec.ts`

## Journey

SUPER_ADMIN triggers POST `/api/admin/content/recreate-from-reference` → dry-run lists candidates → approves → batch via `BatchGenerateAsync` → each candidate CAS-gated → persisted.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| RBAC | SUPER_ADMIN only (ADMIN → 403) |
| DOM | Dry-run output shows candidates |
| DB | New `QuestionDocument` rows with `source=recreated-from-reference` |
| Ship-gate | Raw reference string NEVER leaks into generated body |

## Regression this catches

Raw reference string leaks; ADMIN triggers wet-run; candidates bypass CAS gate.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p1`
