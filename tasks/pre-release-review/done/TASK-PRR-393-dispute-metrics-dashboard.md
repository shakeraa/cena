# TASK-PRR-393: Dispute metrics dashboard (per-template, per-item, per-locale)

**Priority**: P1
**Effort**: S (3-5 days)
**Lens consensus**: persona #9 support + #7 ML safety
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + admin-frontend
**Tags**: epic=epic-prr-j, observability, priority=p1
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Dashboard surfacing dispute rates sliced by template, item, locale. Templates >5% dispute auto-flag for taxonomy review.

## Scope

- Rolling 7-day + 30-day windows.
- Slices: template, item, locale, tier.
- Alert on >5% dispute rate (7-day) per template.

## Files

- `src/admin/full-version/src/pages/observability/dispute-metrics.vue`
- Backend aggregation.

## Definition of Done

- Dashboard surfaces metrics.
- >5% alert fires.
- Full sln green.

## Non-negotiable references

- Memory "Verify data E2E".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-392](TASK-PRR-392-disputed-diagnosis-taxonomy-feedback.md)
