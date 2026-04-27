# TASK-E2E-B-06: Cancel at period end

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/subscription-cancel-end.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Subscriber → `/account/subscription/cancel` → confirm → Stripe `cancel_at_period_end=true` → aggregate stays Active until period ends → on `customer.subscription.deleted` webhook, aggregate → Cancelled, access revoked.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | "Cancel-scheduled" badge; access intact until period end |
| DB | State stays `Active` with `cancelledAtUtc` set; flips to `Cancelled` on period-end webhook |
| Bus | `SubscriptionCancelScheduledV1` now; `SubscriptionCancelledV1` at period end |
| Stripe | `cancel_at_period_end=true` flag set |

## Regression this catches

Cancel-immediately bug (lost paid access); cancel-never bug (billing continues past period); access not revoked post-cancel.

## Done when

- [ ] Spec lands
- [ ] Reactivation flow (within grace period) tested
- [ ] Tagged `@billing @p0`
