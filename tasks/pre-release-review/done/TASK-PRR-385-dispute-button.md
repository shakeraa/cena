# TASK-PRR-385: "This diagnosis seems wrong" dispute button

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #9 support (mandatory for scale)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + backend
**Tags**: epic=epic-prr-j, support, trust, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Every diagnostic result carries a low-friction "this doesn't look right" button that files a dispute. Review flow in [PRR-390](TASK-PRR-390-support-audit-view.md).

## Scope

- Button on `DiagnosticResult.vue`.
- Captures: diagnostic ID, user's reason (optional dropdown + free text), submit timestamp.
- Acknowledges dispute with friendly copy and ETA.
- Backend creates `DisputeTicket` entering support queue.

## Files

- `src/student/full-version/src/components/diagnostic/DisputeButton.vue`
- `src/backend/Cena.StudentApi/Controllers/DiagnosticDisputeController.cs`
- Tests.

## Definition of Done

- Button present on every diagnostic result.
- Submission creates ticket.
- User feedback copy shipgate-compliant.

## Non-negotiable references

- Memory "Labels match data" — when wrong, listen.
- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-390](TASK-PRR-390-support-audit-view.md), [PRR-391](TASK-PRR-391-auto-credit-confirmed-errors.md)
