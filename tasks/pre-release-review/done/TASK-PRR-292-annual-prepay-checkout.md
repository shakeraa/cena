# TASK-PRR-292: Annual prepay toggle at checkout (₪2,490/yr, 17% off)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week)
**Lens consensus**: persona #9 growth (LTV lock, summer-churn mitigation), #10 CFO (cash-flow front-loading)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (billing) + frontend (checkout)
**Tags**: epic=epic-prr-i, commercial, priority=p0, revenue-lever, launch-blocker
**Status**: Ready (pending §5 decision #7 — prepay depth)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

At checkout, offer monthly or annual prepay (₪2,490/yr = 10 months at Premium, 17% off). Front-load cash, lock LTV, reduce Bagrut-cycle summer churn.

## Scope

- Checkout toggle: "monthly ₪249 × 12" vs. "annual ₪2,490 (save ₪498)".
- Savings badge honest (no fake anchors; no "limited time").
- Applies to all three tiers; same 2-months-free ratio.
- Billing engine handles annual plan lifecycle (single charge, 365-day entitlement).
- Refund logic: 30-day money-back guarantee applies pro-rata to annual.
- Downgrade path: at annual renewal, user can switch to monthly.

## Files

- `src/backend/Cena.Domain/Subscriptions/BillingCycle.cs` (new enum)
- `src/student/full-version/src/pages/checkout/index.vue` — toggle
- `src/backend/Cena.StudentApi/Controllers/CheckoutController.cs` — annual flow
- Tests: annual entitlement 365 days, pro-rata refund, savings label correctness.

## Definition of Done

- Annual option selectable on all tiers.
- Charge is single, recurring yearly.
- Pro-rata refund within 30 days works.
- Shipgate scanner passes (no urgency language on toggle).
- Full `Cena.Actors.sln` + build green.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no countdown / "ends soon" on the annual prompt.
- Israel Consumer Protection Law — 14-day right-of-cancellation applies to annual too.
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + test results>"`

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md) — billing engine
- [PRR-306](TASK-PRR-306-refund-workflow.md) — refund pro-rata
- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)
