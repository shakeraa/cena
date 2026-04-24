# TASK-PRR-293: Sibling-add post-purchase UX (₪149 first/second, ₪99 third+)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week)
**Lens consensus**: persona #5 large-family (cap), #9 growth (post-purchase upsell)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + backend (subscription expansion)
**Tags**: epic=epic-prr-i, commercial, priority=p0, expansion-revenue
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

After a parent subscribes, surface a sibling-add CTA on their account screen. Per-seat pricing: +₪149/mo for siblings 1–2, +₪99/mo for siblings 3+ (household cap — persona #5).

## Scope

- Post-purchase onboarding step: "Adding siblings? +₪149 each up to 2; +₪99 each beyond."
- Account settings screen: "Add sibling" button with same pricing.
- Sibling inherits parent's tier (Basic → Basic sibling, etc.) OR can independently choose any tier with the same discount depth (persona #7 flexibility — prefer option B).
- Single consolidated monthly invoice.
- Sibling removal: pro-rata credit for current cycle.
- Arabic/Hebrew/English copy.

## Files

- `src/student/full-version/src/pages/account/siblings.vue` (new)
- `src/backend/Cena.Domain/Subscriptions/HouseholdSubscription.cs` (new)
- `src/backend/Cena.StudentApi/Controllers/HouseholdController.cs`
- Tests: 2-sibling pricing = 249+149+149; 3-sibling = 249+149+149+99; removal pro-rata.

## Definition of Done

- Adding 1st sibling = +₪149 on next invoice.
- Adding 3rd sibling = +₪99 (not ₪149).
- Each sibling has own account/plan; one parent sees all in dashboard.
- Removal pro-rates correctly.
- Full build + sln green.

## Non-negotiable references

- Memory "Labels match data" — sibling price on page matches invoice.
- Israel Consumer Protection Law — clear pricing before confirmation.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + pricing test matrix>"`

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md)
- [PRR-324](TASK-PRR-324-multi-student-household-view.md) — parent dashboard shows siblings
