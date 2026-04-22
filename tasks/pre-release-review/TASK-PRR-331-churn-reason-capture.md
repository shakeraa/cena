# TASK-PRR-331: Downgrade/churn reason-capture workflow

**Priority**: P0 — launch-blocker
**Effort**: S (3-5 days)
**Lens consensus**: persona #9 growth (tier-distribution insight)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + backend
**Tags**: epic=epic-prr-i, observability, priority=p0
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Every cancellation/downgrade captures structured reason. Feeds pricing tuning (PRR-332) and tier-mix analysis.

## Scope

- Exit survey at cancel / downgrade: dropdown (too expensive / kid doesn't use / missing feature / switched to competitor / exam-cycle ended / other) + free-text.
- Response stored session-scoped per ADR-0003 (user can revoke).
- Anonymized aggregates surfaced in unit-economics dashboard.

## Files

- `src/student/full-version/src/components/account/CancellationSurvey.vue`
- `src/backend/Cena.StudentApi/Controllers/ChurnReasonController.cs`
- Tests.

## Definition of Done

- Survey fires on cancel/downgrade.
- Optional for user but visible.
- Aggregates viewable in dashboard.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — retention policy.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-306](TASK-PRR-306-refund-workflow.md), [PRR-330](TASK-PRR-330-unit-economics-dashboard.md)
