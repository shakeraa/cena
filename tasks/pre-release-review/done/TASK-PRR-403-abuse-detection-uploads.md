# TASK-PRR-403: Abuse detection — flag users >200 uploads/mo

**Priority**: P1
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + support
**Tags**: epic=epic-prr-j, abuse-detection, priority=p1
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Users exceeding 200 diagnostic uploads/month flagged for account-sharing investigation. Paired with [PRR-314](TASK-PRR-314-abuse-detection-flag.md) more generally.

## Scope

- Daily batch scan.
- Review queue in admin.
- Pattern features: upload timing, device fingerprint diversity, IP geography variety.
- No auto-action; human review only.

## Files

- `src/backend/Cena.StudentApi/Workers/DiagnosticAbuseScanWorker.cs`
- Tests.

## Definition of Done

- Scan runs daily.
- Flagged users surface in queue.
- No false auto-blocks.

## Non-negotiable references

- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-314](TASK-PRR-314-abuse-detection-flag.md)
