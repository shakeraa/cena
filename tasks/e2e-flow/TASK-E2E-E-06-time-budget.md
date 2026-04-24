# TASK-E2E-E-06: Time-budget control (prr-077)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@parent @ship-gate @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/time-budget.spec.ts`

## Journey

Parent sets 30-min daily cap → child's session approaches cap → UI warns (80% + 100%) but does NOT lock (soft cap per prr-077) → parent gets alert.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Warning at 80% + 100%; no hard lockout |
| DB | `ParentalControlsConfiguredV1` |
| Ship-gate | No hard lockout; no dark-pattern pressure copy (GD-004) |

## Regression this catches

Hard lockout added (ship-gate violation); warning threshold drifts; parent alert drops.

## Done when

- [ ] Spec lands
- [ ] Ship-gate scanner regex runs inline
