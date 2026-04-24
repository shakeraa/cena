# EPIC-E2E-B — Subscription & billing lifecycle

**Status**: Partial (TASK-E2E-001/002/003 shipped as spike)
**Priority**: P0 (revenue-path; a regression here costs money in real time)
**Related ADRs**: [ADR-0057](../../docs/adr/0057-subscription-aggregate-retail-pricing.md), [EPIC-PRR-I](../pre-release-review/EPIC-PRR-I-subscription-pricing-model.md)

---

## Why this exists

Subscription has 4 orthogonal failure surfaces:

1. **Stripe webhook routing** — `metadata.tenant_id` flowing through cleanly
2. **Idempotency** — replayed `checkout.session.completed` must not double-charge
3. **State machine** — Pending → Active → PastDue → Cancelled transitions under real events
4. **Entitlement propagation** — from parent subscription aggregate to per-child session guard (LLM router tier gate)

## Workflows

### E2E-B-01 — Subscription happy path
**Status**: Spike shipped — [TASK-E2E-001](TASK-E2E-001-subscription-happy-path.md). Boundary upgrades scheduled (PRR-436 admin test-probe).

### E2E-B-02 — Payment declined
**Status**: Spec'd — [TASK-E2E-002](TASK-E2E-002-subscription-declined.md). Implementation pending.

### E2E-B-03 — Checkout abandoned
**Status**: Spec'd — [TASK-E2E-003](TASK-E2E-003-subscription-cancel-back.md). Implementation pending.

### E2E-B-04 — Tier upgrade (Basic → Plus mid-cycle)

**Journey**: existing Basic monthly subscriber → `/account/subscription` → upgrade to Plus → Stripe prorates → `checkout.session.completed` fires with `mode=subscription,metadata.upgrade=true` → SubscriptionAggregate records `SubscriptionTierChangedV1` → LLM router sees new tier within 30s (stale-claim gate).

**Boundaries**: DOM (new-tier badge on dashboard), DB (audit trail of the transition, old + new tier timestamped), bus (`SubscriptionTierChangedV1`), Stripe (prorated invoice line item).

**Regression caught**: upgrade charged but aggregate still on old tier → LLM router denies the better model → user paid for nothing.

### E2E-B-05 — Tier downgrade at renewal boundary

**Journey**: Premium annual subscriber → downgrade → effective at renewal (not immediate) → next `invoice.finalized` applies new tier.

**Regression caught**: immediate downgrade bug — customer pays for Premium but gets Plus features before renewal.

### E2E-B-06 — Cancel at period end

**Journey**: subscriber → `/account/subscription/cancel` → confirmation → Stripe flag `cancel_at_period_end=true` → SubscriptionAggregate still Active until period ends → on `customer.subscription.deleted` webhook, aggregate → Cancelled, access revoked.

**Boundaries**: DOM (cancel-scheduled badge), DB (state stays Active with `cancelledAtUtc` set, flips on period end), bus (`SubscriptionCancelledV1`).

**Regression caught**: cancel-immediately bug (lost paid access), or cancel-never bug (billing continues after period end).

### E2E-B-07 — Sibling discount (multi-child household)

**Journey**: parent already subscribed for child A → adds child B → discount applied automatically → Stripe reflects the reduced amount → SubscriptionAggregate records `SiblingDiscountAppliedV1`.

**Boundaries**: DB sibling-discount rule resolution (prr-244 pricing resolver), Stripe invoice shows the discount line, aggregate audit.

**Regression caught**: double-charge (forgot to apply discount), under-charge (applied to wrong seat count).

### E2E-B-08 — Bank transfer (B2B / Israeli-market fallback)

**Journey**: parent picks "bank transfer" on pricing page → POST `/api/me/bank-transfer` (PRR-304) → email with reference number → admin records payment → subscription activated manually.

**Boundaries**: DOM (reference number shown + email copy), DB (BankTransferRequest row), admin UI shows pending row, activation admin action transitions aggregate → Active.

**Regression caught**: bank-transfer requests silently dropped; admin UI doesn't show them; activation not applying tier correctly.

### E2E-B-09 — Institute pricing override (prr-244)

**Journey**: super-admin sets institute-scoped price → parent on that institute sees the override on `/pricing` → checkout charges the overridden amount.

**Boundaries**: DOM (price differs from default), DB (`InstitutePricingOverrideDocument` resolution order), Stripe (correct amount line item).

**Regression caught**: override leaks to wrong tenant; resolver falls through to default; UI shows one price, checkout charges another.

### E2E-B-10 — Webhook idempotency / replay

**Journey**: trigger `checkout.session.completed` twice (Stripe retries a delivery) → second delivery → aggregate state unchanged → no duplicate `SubscriptionActivatedV1` event.

**Boundaries**: Stripe event id uniqueness at the webhook handler (IdempotencyStore), event stream shows exactly 1 activation.

**Regression caught**: double activation → double-count entitlements → wrong tier state.

## Out of scope

- **Fraudulent card detection** — Stripe's problem, not ours
- **Refund flow** — admin-driven, separate workflow (EPIC-E2E-G)
- **Tax / VAT calculation** — Stripe computed

## Definition of Done

- [ ] All 10 workflows green in CI
- [ ] Each runs < 45s with Stripe CLI + Firebase emu
- [ ] Tagged `@billing @p0` — blocks merge if red
- [ ] Sub-agent triage (TASK-E2E-004) demonstrated on a planted regression
