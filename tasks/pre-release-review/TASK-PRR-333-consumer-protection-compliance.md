# TASK-PRR-333: Israel Consumer Protection Law compliance audit

**Priority**: P0 — launch-blocker (legal gate)
**Effort**: M (1 week eng + 1-2 weeks legal)
**Lens consensus**: all personas (statutory)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: legal + backend-dev
**Tags**: epic=epic-prr-i, compliance, legal-gate, priority=p0, launch-blocker
**Status**: Not Started — **legal gate**
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Full compliance with Israel Consumer Protection Law for online subscription services: 14-day right-of-cancellation, clear refund terms, no auto-renewal traps, VAT-inclusive display, transparent pricing disclosure.

## Scope

- Legal audit of pricing page, checkout, T&C, account cancellation flow.
- 14-day cancellation right clearly disclosed and honored independent of the 30-day money-back guarantee.
- Auto-renewal: disclosed + opt-in + clear cancel path.
- Pre-purchase disclosure of: total annual cost for annual plan, VAT inclusion, cancellation rights, refund policy.
- Amendment trail for T&C changes.

## Files

- Audit report (internal doc).
- T&C revisions.
- Checkout copy adjustments.
- `src/student/full-version/src/pages/legal/terms.vue`, `refund-policy.vue`, `privacy.vue` — updates.

## Definition of Done

- Legal audit signed off.
- All surfaces compliant.
- T&C published and linked from checkout + pricing page.

## Non-negotiable references

- Israel Consumer Protection Law.
- Israel VAT regulation.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-294](TASK-PRR-294-money-back-guarantee.md), [PRR-306](TASK-PRR-306-refund-workflow.md), [PRR-305](TASK-PRR-305-hebrew-tax-invoice.md)
