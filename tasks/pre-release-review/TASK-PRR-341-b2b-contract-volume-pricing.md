# TASK-PRR-341: B2B school contract template + volume pricing brackets

**Priority**: P1
**Effort**: M (1-2 weeks legal + pricing model)
**Lens consensus**: persona #8 school coordinator, #10 CFO
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: legal + sales + finance
**Tags**: epic=epic-prr-i, b2b, legal, priority=p1
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Standardize B2B contract: volume pricing brackets, SLA terms, data-protection addendum, teacher-account provisioning, termination / reassignment rights.

## Scope

- Pricing brackets: 100-499 students @ ₪35/mo; 500-1499 @ ₪29/mo; 1500+ @ ₪24/mo.
- Contract template HE + EN.
- DPA addendum (aligned with Israeli Privacy Law + PPL Amendment 13).
- SLA: 99% uptime during school hours.
- Termination clause with data export.

## Files

- Legal docs (not in code repo; filed with legal).
- Pricing reference in `src/backend/Cena.Domain/Subscriptions/` data seed.

## Definition of Done

- Contract template approved by legal.
- Pricing brackets in code tier-seed.
- First pilot school signed on template (success criterion, not launch criterion).

## Non-negotiable references

- Israeli Privacy Law + PPL A13.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-340](TASK-PRR-340-school-sku-plan-definition.md)
