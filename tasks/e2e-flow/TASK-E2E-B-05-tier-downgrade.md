# TASK-E2E-B-05: Tier downgrade at renewal boundary

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/subscription-downgrade.spec.ts`

## Journey

Premium annual subscriber → downgrade → effective at renewal (not immediate) → next `invoice.finalized` webhook applies new tier.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | "Downgrade scheduled" badge visible; entitlements unchanged until period end |
| DB | Pending downgrade recorded; `effectiveAt = periodEnd` |
| Bus | `SubscriptionDowngradeScheduledV1` now; `SubscriptionTierChangedV1` at period end |
| Stripe | `cancel_at_period_end` not set (only downgrade); new plan_id queued |

## Regression this catches

Immediate downgrade bug (paid Premium, got Plus early); downgrade forgotten at period end; refund miscalculation.

## Done when

- [ ] Spec lands
- [ ] IClock-seamed fast-forward of period-end tested
