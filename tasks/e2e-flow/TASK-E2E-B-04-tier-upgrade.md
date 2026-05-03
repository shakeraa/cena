# TASK-E2E-B-04: Tier upgrade (Basic → Plus mid-cycle)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/subscription-upgrade.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Existing Basic monthly subscriber → `/account/subscription` → upgrade to Plus → Stripe prorates → `checkout.session.completed` with `metadata.upgrade=true` → `SubscriptionTierChangedV1` → LLM router sees new tier within 30s.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | New-tier badge on `/home` + `/account/subscription` |
| DB | Aggregate audit trail: old-tier + new-tier timestamped |
| Bus | `SubscriptionTierChangedV1` emitted |
| Stripe | Prorated invoice line item present |
| LLM router | OTel `llm.model` tag reflects Plus-tier model within 30s |

## Regression this catches

Upgrade charged but aggregate still on old tier; LLM router denies upgraded model (customer paid, got nothing); proration calculation wrong.

## Done when

- [ ] Spec lands
- [ ] Tier-claim refresh timing asserted
- [ ] Tagged `@billing @p0`
