# TASK-E2E-I-04: Consent audit export completeness (prr-130)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/consent-audit-export.spec.ts`

## Journey

Over a 6-month test window (IClock) → student undergoes ~12 consent flips → admin CSV export → 12 rows with timestamp + actor + scope + new-state.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| CSV | Column structure + row count = 12 |
| Order | Newest last |
| Tenant | Export filter honored |

## Regression this catches

Rows missing from export; order scrambled; scope labels inconsistent.

## Done when

- [ ] Spec lands
