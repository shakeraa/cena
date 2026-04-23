# TASK-E2E-J-05: Stripe webhook replay — burst variant

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @billing @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/stripe-webhook-burst.spec.ts`

## Journey

5 webhook deliveries of the same event within 100ms → DB shows exactly 1 activation event; Stripe retry strategy respected. Variant of [B-10](TASK-E2E-B-10-webhook-idempotency.md).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | Event count = 1 per idempotency key |
| HTTP | All 5 return 200 |

## Regression this catches

Idempotency store race; multiple activations from burst delivery.

## Done when

- [ ] Spec lands
- [ ] Tagged `@p0`
