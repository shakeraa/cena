# TASK-E2E-H-06: Break-glass overlay scoped to tenant (prr-220)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @admin @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/break-glass-tenant.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Admin disables a feature family for institute A (break-glass) → institute B continues unaffected.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | BreakGlass row per tenant |
| Runtime | Feature-flag gate respects tenant-scoped override |
| Admin | B's admin cannot override A's break-glass |

## Regression this catches

Break-glass flips globally; cross-admin override.

## Done when

- [ ] Spec lands
