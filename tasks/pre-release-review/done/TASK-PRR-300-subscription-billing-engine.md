# TASK-PRR-300: Subscription billing engine (plan lifecycle)

**Priority**: P0 — launch-blocker
**Effort**: L (2-3 weeks)
**Lens consensus**: all personas implicit (no commerce without billing)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (senior) + DBA
**Tags**: epic=epic-prr-i, billing, priority=p0, launch-blocker
**Status**: Ready (pending §5 decision #1 final price)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Subscription aggregate + lifecycle workflow: trial → active → past-due → cancelled → refunded. Production-grade (memory "No stubs"), event-sourced per project convention.

## Scope

- Aggregate `Subscription { id, parentAccountId, planTierId, billingCycle, startsAt, renewsAt, status, cancelledAt, entitlements }`.
- State transitions event-sourced; every change emits `SubscriptionChangedEvent`.
- Status enum: `trial`, `active`, `past_due`, `cancelled`, `refunded`, `expired`.
- Past-due handling: retry 3x over 7 days, then downgrade to free-locked-out or cancel (configurable).
- Household support: parent → [students], siblings inherit entitlement pointer.
- Not coupled to Stripe (PRR-301 adapts) — domain stays payment-provider-agnostic.
- Idempotent webhooks via external transaction ID.

## Files

- `src/backend/Cena.Domain/Subscriptions/Subscription.cs` (aggregate)
- `src/backend/Cena.Domain/Subscriptions/SubscriptionEvents.cs`
- `src/backend/Cena.Domain/Subscriptions/SubscriptionLifecycle.cs`
- `src/backend/Cena.StudentApi/Workers/SubscriptionRenewalWorker.cs`
- DB migration (subscriptions, subscription_events, household_membership tables)
- Tests: full state-machine coverage; event replay.

## Definition of Done

- All state transitions covered by tests (minimum 20 transition scenarios).
- Event replay reconstructs state deterministically.
- Webhook idempotency verified (duplicate Stripe webhook = single state change).
- No payment-provider strings in `Cena.Domain`.
- Full sln build + test green.

## Non-negotiable references

- Project convention: event sourcing for state changes (CLAUDE.md).
- Memory "No stubs — production grade".
- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — multi-tenant isolation (subscriptions scoped to institute where relevant).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + state-machine test report>"`

## Related

- [PRR-301](TASK-PRR-301-stripe-integration.md) — payment adapter
- [PRR-302](TASK-PRR-302-bit-integration.md), [PRR-303](TASK-PRR-303-paybox-integration.md)
- [PRR-306](TASK-PRR-306-refund-workflow.md)
- [PRR-310](TASK-PRR-310-subscription-tier-propagation.md) — consumes entitlements
