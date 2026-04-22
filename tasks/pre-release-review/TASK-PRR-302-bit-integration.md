# TASK-PRR-302: Bit payment integration

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks engineering + 3-6 weeks Bit procurement)
**Lens consensus**: persona #6 Arabic-Israeli + #5 Haredi (low credit-card penetration segments)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + payments-procurement (Bit / Bank Hapoalim)
**Tags**: epic=epic-prr-i, billing, payments, priority=p0, vendor-gate, launch-blocker
**Status**: Not Started — **vendor-procurement gate**
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Integrate Bit (Israeli P2P-dominant payment) as a checkout option. Closes the accidental-exclusion gap for segments with lower credit-card penetration.

## Scope

- Merchant application with Bit (bank-owned, typically 4-6 weeks).
- Bit SDK / API integration for one-time + recurring where supported (Bit recurring may be limited — document gap).
- If recurring unsupported, fall back: Bit for initial payment, link card on file for renewals.
- Webhook into billing engine identical to Stripe contract.
- VAT + Hebrew invoice flow (PRR-305).

## Files

- `src/backend/Cena.Infra/Payments/Bit/BitCheckoutAdapter.cs`
- `src/backend/Cena.Infra/Payments/Bit/BitWebhookHandler.cs`
- Tests: webhook contract parity with Stripe adapter.

## Definition of Done

- Bit selected at checkout → active subscription in billing engine.
- Parity test with Stripe path (same `Subscription` shape post-webhook).
- Recurring limitations documented if present.
- No Bit credentials committed.
- Full sln + integration green.

## Non-negotiable references

- Memory "No stubs — production grade".
- CLAUDE.md — no secrets committed.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + Bit sandbox test>"`

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md)
- [PRR-303](TASK-PRR-303-paybox-integration.md)
