# TASK-E2E-B-07: Sibling discount (multi-child household)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/sibling-discount.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Parent already subscribed for child A → adds child B via `/parent/children/add` → discount applied automatically → Stripe invoice reflects reduced amount → `SiblingDiscountAppliedV1` recorded.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Pricing UI shows discount applied on second child |
| DB | Sibling-discount rule resolution (prr-244 pricing resolver); aggregate audit |
| Bus | `SiblingDiscountAppliedV1` emitted |
| Stripe | Invoice line item shows discount |

## Regression this catches

Double-charge (forgot to apply); under-charge (wrong seat count); discount leaks to unrelated parent who shares email prefix.

## Done when

- [ ] Spec lands
- [ ] Third and fourth child pricing tested (tier stacking)
