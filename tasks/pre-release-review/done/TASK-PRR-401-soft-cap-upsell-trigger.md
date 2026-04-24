# TASK-PRR-401: Soft-cap upsell trigger (shipgate-compliant)

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO + shipgate
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + frontend
**Tags**: epic=epic-prr-j, upsell, shipgate, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

When a Premium user hits 100-upload soft cap, backend emits trigger → frontend shows [PRR-386](TASK-PRR-386-soft-cap-reached-ux.md) modal.

## Scope

- Backend event `DiagnosticSoftCapReached { studentId, count, tier, periodEnd }`.
- Frontend consumer pops modal.
- Once per cap period.

## Files

- `src/backend/Cena.Diagnostic/Events/DiagnosticSoftCapReached.cs`
- Frontend event listener.
- Tests.

## Definition of Done

- Event fires exactly once per period.
- Modal shows.
- Full sln green.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Shipgate scanner.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-386](TASK-PRR-386-soft-cap-reached-ux.md), [PRR-400](TASK-PRR-400-per-tier-upload-counter.md)
