# TASK-PRR-412: Photo-deletion SLA enforcement (5 min) + hash-ledger retention

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #2 parent, #5 compliance
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + devops
**Tags**: epic=epic-prr-j, privacy, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Guaranteed photo-deletion within 5 minutes of upload. Exception: disputed diagnostics persist evidence per [PRR-390](TASK-PRR-390-support-audit-view.md) for 30 days. Hash-ledger retained (not photo) for abuse-detection / audit.

## Scope

- Delete worker fires per upload + 5 min.
- Monitoring + alert on delete-failure.
- Verifiable: periodic audit job checks no photo older than 5 min (except dispute-snapshot).
- Hash ledger retained; maps to diagnostic ID + student + timestamp.

## Files

- `src/backend/Cena.StudentApi/Workers/PhotoDeletionWorker.cs`
- Monitoring alert config.
- Tests + audit job.

## Definition of Done

- 5-min SLA met 99.9% of uploads.
- Audit job reports violation.
- Hash ledger persists.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).
- PPL Amendment 13.
- Memory "Labels match data" — if policy says 5 min, it's 5 min.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-390](TASK-PRR-390-support-audit-view.md), [EPIC-PRR-H PRR-246](EPIC-PRR-H-student-input-modalities.md)
