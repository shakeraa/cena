# TASK-PRR-345: Pricing email template suite (welcome / renewal / past-due / cancellation / refund confirm) (TAIL)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week content + 3-5 days eng)
**Lens consensus**: tail
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: content + backend
**Tags**: epic=epic-prr-i, transactional-email, priority=p0, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

All transactional subscription emails in HE/AR/EN: welcome, trial-ending (if trial), renewal-upcoming, past-due, cancellation-confirm, refund-confirm.

## Scope

- Templates for each lifecycle moment.
- Honest framing (memory "Honest not complimentary" — no guilt-trip on cancellation emails).
- Correct invoice link embedded where relevant.
- Unsubscribe honored (statutory).
- Legal-reviewed cancellation + refund language.

## Files

- `emails/subscription-{welcome,renewal,past-due,cancellation,refund}.{he,ar,en}.html`
- Worker wiring to lifecycle events.
- Tests: locale, rendering, link correctness.

## Definition of Done

- All templates rendered, legal-approved.
- Wired to subscription lifecycle events.
- Locale correct.

## Non-negotiable references

- Israel direct-marketing law — unsubscribe.
- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md), [PRR-295](TASK-PRR-295-two-week-progress-report-email.md), [PRR-323](TASK-PRR-323-weekly-parent-digest.md)
