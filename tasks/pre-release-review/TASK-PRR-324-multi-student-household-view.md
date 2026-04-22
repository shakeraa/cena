# TASK-PRR-324: Multi-student household view (parent sees all siblings)

**Priority**: P0 — launch-blocker (sibling-discount is Premium feature)
**Effort**: M (1 week)
**Lens consensus**: persona #5 large families, #6 Arabic-Israeli (multi-kid households common), #9 growth (LTV expansion)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + backend
**Tags**: epic=epic-prr-i, parent-ux, priority=p0, multi-child
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Parent dashboard shows all linked students in one view. Tab switcher per student + aggregate household view (total time-on-task, household readiness snapshot).

## Scope

- Parent aggregate read model extended to household.
- Student-switcher UI; each student preserves own mastery map + diagnostic summary.
- Household view: collapsed per-student summary card + aggregate time-on-task.
- Privacy: each student's session data private to that student; parent sees aggregates only (no raw session transcripts).

## Files

- `src/parent/src/pages/household.vue`
- `src/backend/Cena.StudentApi/Controllers/HouseholdDashboardController.cs`
- Tests.

## Definition of Done

- Parent with 2+ linked students sees all in one view.
- No cross-household leakage.
- Privacy: no raw transcripts exposed.
- Full sln green.

## Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md).
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md), [PRR-293](TASK-PRR-293-sibling-add-post-purchase.md)
