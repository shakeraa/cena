# TASK-PRR-343: Feature fencing — parent dashboard + tutor-handoff NOT in school SKU

**Priority**: P1
**Effort**: S (3-5 days)
**Lens consensus**: persona #8 (pricing-leak prevention)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-i, b2b, feature-fencing, priority=p1
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Enforce: school-SKU accounts cannot access parent dashboard or tutor-handoff PDF. Feature-set non-overlap prevents parents + schools discovering each other's prices.

## Scope

- Authorization filter on parent-dashboard endpoints.
- Authorization filter on tutor-handoff-PDF endpoint.
- Tier-data flag `visibleInSku: retail | b2b | both`.
- Tests verifying 403 for school-SKU accessing parent-only features.

## Files

- `src/backend/Cena.StudentApi/Authorization/SkuFeatureAuthorizer.cs`
- Update controllers with filter.
- Tests.

## Definition of Done

- School-SKU user receives 403 on parent-dashboard endpoints.
- Retail user unaffected.
- Full sln green.

## Non-negotiable references

- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-340](TASK-PRR-340-school-sku-plan-definition.md), [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md), [PRR-325](TASK-PRR-325-tutor-handoff-pdf.md)
