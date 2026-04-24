# TASK-PRR-305: VAT-inclusive Israel pricing + Hebrew tax invoice generation

**Priority**: P0 — launch-blocker (legal requirement)
**Effort**: M (1 week eng + 2-3 weeks legal/accountant)
**Lens consensus**: persona #10 CFO (compliance), all payment-adjacent personas
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + accountant + legal
**Tags**: epic=epic-prr-i, billing, compliance, priority=p0, legal-gate, launch-blocker
**Status**: Not Started — **legal gate** (accountant review)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Generate compliant Hebrew tax invoices (חשבונית מס) for every charge. VAT 17% inclusive; structure per Israel Tax Authority requirements.

## Scope

- Invoice template in Hebrew (primary), Arabic/English translations as additional renders.
- Required fields: Cena legal entity + tax ID, customer name + address, sequential invoice number, date, line items with VAT breakdown, VAT rate + amount, total.
- Auto-generated per successful charge (Stripe / Bit / PayBox / bank transfer).
- Stored durably; customer can re-download from account.
- Annual invoice for annual plans; 12 monthly invoices for monthly plans.
- Accountant-signed-off template before going live.

## Files

- `src/backend/Cena.Infra/Invoicing/HebrewTaxInvoiceGenerator.cs`
- `src/backend/Cena.Domain/Invoicing/Invoice.cs`
- PDF template (HTML → PDF pipeline)
- `src/student/full-version/src/pages/account/invoices.vue` — re-download
- Tests: tax-math correctness, sequential numbering, VAT breakdown.

## Definition of Done

- Every successful charge produces a stored invoice.
- VAT math correct to agora (0.01 ₪).
- Sequential numbering without gaps.
- Accountant sign-off recorded in PR.
- Arabic + English renders available.

## Non-negotiable references

- Israel Tax Authority invoice regulations.
- Memory "Labels match data" — invoice = what was charged.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + accountant signoff link>"`

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md)
- [PRR-301](TASK-PRR-301-stripe-integration.md)
