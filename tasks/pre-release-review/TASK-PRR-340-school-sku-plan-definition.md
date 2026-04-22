# TASK-PRR-340: B2B school SKU — plan definition + admin dashboard

**Priority**: P1 (launch+1 acceptable; but critical to avoid pricing leaks if any school pilot near)
**Effort**: L (3-4 weeks)
**Lens consensus**: persona #8 school coordinator
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + frontend + sales
**Tags**: epic=epic-prr-i, b2b, priority=p1
**Status**: Ready (pending §5 decision #4 — launch or launch+1)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

School SKU: ~₪35/student/mo, admin dashboard, teacher-assigned practice, SSO, classroom metrics. Deliberately NON-overlapping with retail Premium features (no parent dashboard, no tutor-handoff PDF) to prevent pricing-leak embarrassment.

## Scope

- Plan type `b2b_school` with volume-bracket pricing (100-499 / 500-1499 / 1500+ students).
- Admin dashboard: class management, teacher onboarding, student roster CSV, per-class metrics.
- Feature fencing: parent-dashboard endpoints REJECT school-SKU accounts.
- Teacher role distinct from student role.
- Invoice flow: annual with net-30 payment terms (vs. parent prepay).

## Files

- `src/admin/full-version/src/pages/schools/`
- `src/backend/Cena.Domain/Subscriptions/SchoolSubscription.cs`
- Tests: feature-fencing enforces boundaries.

## Definition of Done

- School admin can onboard 100 students and assign teachers.
- Parent-dashboard endpoints return 403 for school accounts.
- Net-30 invoice flow works.
- Full sln green.

## Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-341](TASK-PRR-341-b2b-contract-volume-pricing.md), [PRR-342](TASK-PRR-342-sso-integration.md), [PRR-343](TASK-PRR-343-school-feature-fencing.md)
