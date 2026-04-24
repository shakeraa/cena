# TASK-PRR-392: Weekly disputed-diagnosis review → taxonomy feedback

**Priority**: P1
**Effort**: S (3-5 days eng + ongoing SME time)
**Lens consensus**: persona #9 support (iteration loop)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: SME + backend (pipeline)
**Tags**: epic=epic-prr-j, governance, priority=p1
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Weekly review of confirmed system-error disputes → feeds taxonomy updates ([PRR-375](TASK-PRR-375-taxonomy-governance.md)). Regression test added when a template is updated to cover the case.

## Scope

- Weekly report: disputes → by template, by break-type, by locale.
- SME review cadence.
- Auto-create regression test case for updated templates.

## Files

- `src/backend/Cena.StudentApi/Workers/DisputeReviewReportWorker.cs`
- Admin UI for SME review.

## Definition of Done

- Weekly report fires.
- SME workflow in place.
- Regression tests auto-generated.

## Non-negotiable references

- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-375](TASK-PRR-375-taxonomy-governance.md), [PRR-390](TASK-PRR-390-support-audit-view.md)
