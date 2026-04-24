# TASK-E2E-B-09: Institute pricing override (prr-244)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @tenant @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/institute-pricing-override.spec.ts`

## Journey

SUPER_ADMIN sets institute-scoped price for institute A → parent on institute A sees the override on `/pricing` → checkout charges the overridden amount. Parent on institute B sees default price.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | `/pricing` shows override for A, default for B |
| DB | `InstitutePricingOverrideDocument` resolved by tenant |
| Stripe | Line-item amount matches override for A, default for B |
| Bus | `InstitutePricingOverriddenV1` emitted on admin write; SIEM audit row present |

## Regression this catches

Override leaks to wrong tenant (charges wrong customer); resolver falls through to default unexpectedly; UI shows override but checkout charges default (or vice versa).

## Done when

- [ ] Spec lands
- [ ] Cross-tenant parent test (override applied to A NOT visible to B) asserted
- [ ] Tagged `@tenant @p0`
