# TASK-E2E-H-05: Institute pricing override stays within its institute (prr-244)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-H](EPIC-E2E-H-multi-tenant-isolation.md)
**Tag**: `@tenant @billing @compliance @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/institute-pricing-tenant-scope.spec.ts`

## Journey

See [TASK-E2E-B-09](TASK-E2E-B-09-institute-pricing-override.md). This variant runs purely for tenant-isolation focus — asserts that cross-tenant parent of B sees default while A's override in place.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Resolver | `IInstitutePricingResolver` honors caller's tenant |
| DB | Override table queried with tenant predicate |
| Cache | Keyed by tenant |

## Regression this catches

Override leaks (wrong price charged); cache cross-contamination.

## Done when

- [ ] Spec lands
- [ ] Coupled to B-09 test run; doesn't duplicate, just adds the B-side assertion
