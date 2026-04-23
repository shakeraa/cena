# TASK-E2E-I-06: Age-band field filter consistency (prr-052)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @parent @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/age-band-consistency.spec.ts`

## Journey

Child ages 12 → 13 → 14 (IClock) → dashboard field set shrinks at each threshold → prior-year exports don't leak fields through.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Field set at each age threshold matches policy |
| Export | Historical snapshot respects CURRENT age-band, not snapshot's original |

## Regression this catches

Field hidden on current dashboard but visible in historical export; band transition not triggered until next session.

## Done when

- [ ] Spec lands
