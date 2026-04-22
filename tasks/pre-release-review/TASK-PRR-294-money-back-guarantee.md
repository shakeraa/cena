# TASK-PRR-294: 30-day money-back guarantee display + refund workflow

**Priority**: P0 — launch-blocker
**Effort**: S (3-5 days)
**Lens consensus**: persona #1 cost-conscious (proof-of-value pre-pays first invoice)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + backend (refund API)
**Tags**: epic=epic-prr-i, commercial, priority=p0, trust, launch-blocker
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Display 30-day money-back guarantee prominently on pricing page + checkout. Backend refund workflow is separate ([PRR-306](TASK-PRR-306-refund-workflow.md)).

## Scope

- Pricing-page badge: "30 ימי ערובת החזרה" / "ضمان استرداد 30 يوماً" / "30-day money-back guarantee".
- Checkout confirmation reiterates guarantee.
- Account → Billing screen shows "request refund" CTA during first 30 days.
- Legal copy reviewed by counsel before publication.
- Clear separation from Israel Consumer Protection Law's 14-day statutory cancellation (guarantee is additive, more generous).

## Files

- `src/student/full-version/src/components/pricing/GuaranteeBadge.vue` (new)
- `src/student/full-version/src/pages/account/billing.vue` — refund CTA during window
- `src/backend/Cena.StudentApi/Controllers/RefundController.cs` (stub, full impl in PRR-306)
- i18n copy HE/AR/EN — **legal-reviewed**.

## Definition of Done

- Badge visible on pricing page and checkout.
- Refund CTA visible during day 1–30; hidden after.
- Copy approved by legal (trail linked in PR).
- Full build green.

## Non-negotiable references

- Israel Consumer Protection Law — guarantee layers above 14-day statutory right; no trap language.
- Memory "Labels match data" — what's promised is what backend enforces.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + legal-review link>"`

## Related

- [PRR-306](TASK-PRR-306-refund-workflow.md) — refund backend
- [PRR-333](TASK-PRR-333-consumer-protection-compliance.md) — consumer-protection law compliance audit
