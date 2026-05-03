# TASK-PRR-320: Parent dashboard MVP (Hebrew)

**Priority**: P0 — launch-blocker for Premium tier
**Effort**: L (2-3 weeks)
**Lens consensus**: persona #2 high-SES parent (trust artifact), #8 school coordinator (similar but separate SKU)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend-dev (Vuexy) + backend (aggregation APIs)
**Tags**: epic=epic-prr-i, parent-ux, priority=p0, launch-blocker, premium-value-driver
**Status**: Ready (couples to EPIC-PRR-C consent)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Premium-tier parent sees their student's (or students') progress: topics practiced, mastery map per Bagrut target, readiness-score delta, diagnostic summary, time-on-task. Hebrew first ([PRR-321](TASK-PRR-321-parent-dashboard-arabic.md) adds Arabic).

## Scope

- Aggregated read model per parent account.
- Views: overview card, per-student tab, topic mastery heatmap, weekly activity timeline, diagnostic summary (NOT individual photo — summaries only).
- No misconception details beyond 30 days (ADR-0003).
- Data never traces to photos; only derived session-scoped aggregates.
- Parent sees only THEIR linked students; cross-household isolation enforced.
- Consent flow from EPIC-PRR-C gates access under-16 students.
- Must look professional (persona #2 trust) — over-polish vs. student UI.

## Files

- `src/parent/` (new SPA or subroute inside student-spa)
- `src/backend/Cena.Domain/Parenting/ParentDashboard.cs`
- `src/backend/Cena.StudentApi/Controllers/ParentDashboardController.cs`
- Tests: isolation between households, aggregation correctness, consent enforcement.

## Definition of Done

- Hebrew dashboard renders with real data.
- Cross-household data never leaked.
- Consent from EPIC-PRR-C enforced.
- Contrast audit passes.
- Full sln green.

## Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — multi-tenant.
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).
- Memory "Verify data E2E".
- Memory "Primary color locked".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-321](TASK-PRR-321-parent-dashboard-arabic.md) — Arabic parity (launch-blocker per persona #6)
- [PRR-322](TASK-PRR-322-parent-dashboard-english.md)
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md)
