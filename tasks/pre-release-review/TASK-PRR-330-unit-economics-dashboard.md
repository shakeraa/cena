# TASK-PRR-330: Unit-economics dashboard (per-tier contribution margin)

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #10 CFO
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + data-eng + finance
**Tags**: epic=epic-prr-i, observability, commercial, priority=p0
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Weekly automated dashboard: per-tier ARPU, COGS, contribution margin, LTV, churn, CAC payback. Memory "Honest not complimentary" — real numbers with CIs, not aspirational.

## Scope

- Pulls: subscription events, LLM usage cost, OCR cost, infra cost, CAC from marketing attribution.
- Metrics per tier: gross revenue, net-after-VAT, COGS (LLM + OCR + infra allocated), contribution margin $/mo, LTV, churn %.
- Weekly Slack/email summary to decision-holder.
- Alert when Premium contribution <$20/mo for 2 consecutive weeks.
- Honest-numbers framing: CIs on LTV, not point estimates.

## Files

- `src/admin/full-version/src/pages/finance/unit-economics.vue`
- `src/backend/Cena.AdminApi/Controllers/UnitEconomicsController.cs`
- `src/backend/Cena.StudentApi/Workers/UnitEconomicsRollupWorker.cs`
- Tests.

## Definition of Done

- Dashboard shows current-week per-tier numbers.
- Weekly summary delivers.
- Alert fires on margin compression.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary" — CIs mandatory on LTV.
- Memory "Verify data E2E" — numbers match actual DB.

## Reporting

complete via: standard queue complete.

## Related

- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)
