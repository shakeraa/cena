# TASK-E2E-B-10: Webhook idempotency / replay

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @resilience @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/webhook-idempotency.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Trigger `checkout.session.completed` 2–5× (simulating Stripe retry) → second+ deliveries → aggregate state unchanged → exactly 1 `SubscriptionActivatedV1` event.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | `IdempotencyStore` records the Stripe event id; aggregate event count == 1 |
| Bus | Subscribe before trigger; receive exactly 1 activation event within the window |
| HTTP | All webhook calls return 200 (don't throw on replay) |

## Regression this catches

Double activation → double entitlement; idempotency store key collision across tenants; webhook handler throws on replay (Stripe retries forever).

## Done when

- [ ] Spec lands
- [ ] 5-burst variant + out-of-order variant both tested
- [ ] Tagged `@resilience @p0`
