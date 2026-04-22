# TASK-PRR-423: Accuracy-audit sampling (weekly 1% human review)

**Priority**: P1
**Effort**: M (1 week eng + ongoing SME)
**Lens consensus**: persona #7 ML safety, #9 support
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + math-SME
**Tags**: epic=epic-prr-j, quality-assurance, priority=p1
**Status**: Ready (SME gate)
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Weekly random 1% sample of diagnostics reviewed by math-SME. Error rate tracked; regression test generated for confirmed errors.

## Scope

- Random-sample worker selects 1% weekly.
- Review UI for SME (similar to support audit view).
- Confirmed-error → auto-regression test.
- Rolling error-rate metric.

## Files

- `src/backend/Cena.StudentApi/Workers/AccuracyAuditSampler.cs`
- `src/admin/full-version/src/pages/quality/accuracy-audit.vue`
- Tests.

## Definition of Done

- 1% sampled weekly.
- SME review workflow.
- Regression test auto-created.

## Non-negotiable references

- Memory "Verify data E2E".
- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-392](TASK-PRR-392-disputed-diagnosis-taxonomy-feedback.md)
