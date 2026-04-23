# TASK-PRR-332: A/B testing harness for pricing-page variants + Plus feature-mix

**Priority**: P0 — launch-blocker (first 90 days = discovery phase per persona #9)
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #9 growth, #3 engineer-parent (decoy legitimacy monitoring)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (experiment framework) + frontend
**Tags**: epic=epic-prr-i, experimentation, priority=p0
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Ship with deterministic-bucketed A/B harness. Enables first-90-days pricing-page + Plus-feature-mix discovery (persona #9 thesis: don't lock pricing on day 1).

## Scope

- Bucket by parentAccountId hash → stable per-user.
- Experiment manifest: variant set, traffic split, primary metric, stop conditions.
- Sample initial experiments: Plus-dashboard-included vs. excluded; diagnostic-cap 10 vs. 20/mo; anchor ₪249 vs. ₪279 Premium.
- Metrics auto-rolled into unit-economics dashboard.
- Kill-switch + staged rollout.

## Files

- `src/backend/Cena.Infra/Experiments/ExperimentManifest.cs`
- `src/backend/Cena.Infra/Experiments/Bucketer.cs`
- `src/admin/full-version/src/pages/experiments/pricing.vue`
- Tests: deterministic bucketing, kill-switch.

## Definition of Done

- Harness wired into pricing page.
- First experiment (Plus feature-mix) live on launch.
- Results visible to decision-holder within 48h of exposure.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary" — result framing with CIs.
- Privacy: bucketing data scoped per ADR-0003.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-330](TASK-PRR-330-unit-economics-dashboard.md)
