# TASK-PRR-313: Graceful soft-cap upsell UX (shipgate-compliant)

**Priority**: P0 — launch-blocker
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO (soft-cap, not hard-block), #4 student (never punitive)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + copy/content (HE/AR/EN shipgate-compliant)
**Tags**: epic=epic-prr-i, ux, upsell, priority=p0, shipgate
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

When a Premium user hits the soft cap (100 diagnostic/mo or 2000 hints/mo), show positive upsell UX ("you're in the top 1%! want a tutor session?") — NOT a hard block or scarcity framing.

## Scope

- Modal triggered at soft-cap threshold.
- Positive framing ("top 1%", "busy month!") — no urgency / scarcity / streak.
- Actions: "book a tutor session" (links to partner / marketplace when available) OR "continue anyway" (still allows; this is soft).
- Hard cap (300/mo Premium, 300/mo diagnostic) surfaces "contact support" path.
- Shipgate scanner must pass every copy variant.
- HE / AR / EN locale.

## Files

- `src/student/full-version/src/components/upsell/SoftCapModal.vue`
- i18n copy HE/AR/EN
- Tests: shipgate scan green, trigger at correct threshold, no hard-block behavior.

## Definition of Done

- Soft-cap modal appears at 100 uploads/mo.
- Copy passes shipgate.
- User can continue past soft cap (it's soft).
- Hard cap at 300 routes to "contact support".

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- [`docs/engineering/shipgate.md`](../../docs/engineering/shipgate.md).
- Memory "Ship-gate banned terms".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-312](TASK-PRR-312-per-tier-photo-diagnostic-caps.md), [PRR-400](TASK-PRR-400-per-tier-upload-counter.md)
- [PRR-401](TASK-PRR-401-soft-cap-upsell-trigger.md)
