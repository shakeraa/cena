# TASK-PRR-303: PayBox payment integration

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks + 2-4 weeks procurement)
**Lens consensus**: persona #6 + #5 (alternative payment methods)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + payments-procurement
**Tags**: epic=epic-prr-i, billing, payments, priority=p0, vendor-gate
**Status**: Deferred to post-launch 2026-04-23 — **blocked on PayBox merchant contract**; cannot proceed without procurement. PRR-304 bank-transfer ships as the alternative-payment path for launch.
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Integrate PayBox as checkout option. Parity contract with Stripe + Bit adapters.

## Scope

- Merchant application.
- SDK integration.
- Webhook → billing engine.
- VAT + Hebrew invoice flow.

## Files

- `src/backend/Cena.Infra/Payments/PayBox/PayBoxCheckoutAdapter.cs`
- `src/backend/Cena.Infra/Payments/PayBox/PayBoxWebhookHandler.cs`
- Tests.

## Definition of Done

- PayBox selected at checkout → active subscription.
- Parity with Stripe + Bit.
- Full sln green.

## Non-negotiable references

- Memory "No stubs — production grade".
- CLAUDE.md — no secrets committed.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sandbox test>"`

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md), [PRR-302](TASK-PRR-302-bit-integration.md)
