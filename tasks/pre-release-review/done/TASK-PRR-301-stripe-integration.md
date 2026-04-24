# TASK-PRR-301: Stripe integration (credit card recurring + Israel VAT)

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: all personas (payments core)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + finance (Israel VAT config)
**Tags**: epic=epic-prr-i, billing, payments, priority=p0, launch-blocker
**Status**: Ready (pending Stripe Israel account provisioning)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Adapt Stripe Checkout + Subscriptions to drive Cena's billing engine (PRR-300). Support Israel VAT-inclusive pricing, monthly + annual cycles, failed-payment retries, secure webhook handling.

## Scope

- Stripe Products + Prices seeded for Basic/Plus/Premium × monthly/annual × ILS.
- Checkout session creation from student-api.
- Webhooks: `checkout.session.completed`, `customer.subscription.updated`, `invoice.payment_failed`, `charge.refunded` → drive `Subscription` aggregate transitions.
- Webhook signature verification mandatory (no unsigned accepted).
- VAT 17% inclusive display handled (Stripe Tax evaluation deferred — v1 manual).
- Customer portal enabled for self-service downgrades/cancellations.
- PCI scope: client-side tokenization only; no card data touches our servers.
- No test keys in production `.env`; secrets via key vault (CLAUDE.md security rules).

## Files

- `src/backend/Cena.Infra/Payments/Stripe/StripeCheckoutAdapter.cs`
- `src/backend/Cena.Infra/Payments/Stripe/StripeWebhookHandler.cs`
- `src/backend/Cena.StudentApi/Controllers/StripeWebhookController.cs`
- Tests: webhook signature verify, each event→transition mapping, duplicate webhook idempotency.

## Definition of Done

- Checkout → webhook → `Subscription` active in <10s end-to-end.
- Failed payment triggers past_due, retry worker fires correctly.
- Refund webhook fires refund transition.
- Webhook signature mandatory (unsigned rejected).
- No secrets in repo (verified via secret scanner).
- Full sln + integration test green.

## Non-negotiable references

- CLAUDE.md — "NEVER commit secrets" + `npx @claude-flow/cli@latest security scan` after payment changes.
- Memory "No stubs — production grade".
- Israel VAT 17% regulation.
- PCI-DSS SAQ-A scope only (tokenization path).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + webhook verify test>"`

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md)
- [PRR-305](TASK-PRR-305-hebrew-tax-invoice.md)
- [PRR-306](TASK-PRR-306-refund-workflow.md)
