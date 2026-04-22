# TASK-PRR-306: Refund workflow (30-day money-back automation)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week)
**Lens consensus**: persona #1 trust (guarantee must be real), #10 CFO (abuse control)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + support-lead (dispute UX)
**Tags**: epic=epic-prr-i, billing, priority=p0, trust, launch-blocker
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Self-service refund request during first 30 days. Full refund for monthly; pro-rata refund for annual (used-days credited against full refund). Auto-approve within guarantee window; finance reviews beyond.

## Scope

- "Request refund" CTA in account → billing during day 1–30.
- Optional reason capture (free-text + dropdown) → feeds churn-reason workflow ([PRR-331](TASK-PRR-331-churn-reason-capture.md)).
- Auto-approval if within 30 days AND not abuse-flagged.
- Abuse rule: refund denied if account has used >500 diagnostic uploads or >50 hint requests in the window (indicates "use-then-refund" gaming) — configurable.
- Refund flows through payment adapter (Stripe API / Bit / PayBox support).
- Subscription transitions to `refunded`; account downgraded to free-locked after refund clears.
- Email confirmation in parent's locale.

## Files

- `src/backend/Cena.StudentApi/Controllers/RefundController.cs` (full impl)
- `src/backend/Cena.Domain/Subscriptions/RefundPolicy.cs`
- `src/student/full-version/src/pages/account/request-refund.vue`
- Tests: within-window auto-approve, pro-rata annual math, abuse-flagged denial.

## Definition of Done

- Refund-within-window auto-approves and credits card.
- Annual pro-rata math correct.
- Abuse rule denies use-then-refund pattern.
- Confirmation email sent in locale.
- Full sln green.

## Non-negotiable references

- Israel Consumer Protection Law — 14-day statutory right inviolable (ours is additive).
- Memory "Labels match data" — what was promised is refunded.
- Memory "Honest not complimentary" — honest denial reason if abuse rule fires.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + refund test matrix>"`

## Related

- [PRR-294](TASK-PRR-294-money-back-guarantee.md)
- [PRR-331](TASK-PRR-331-churn-reason-capture.md)
- [PRR-333](TASK-PRR-333-consumer-protection-compliance.md)
