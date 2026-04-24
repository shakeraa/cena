# TASK-PRR-391: Auto-credit on confirmed system errors

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #9 support (fast resolution)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + support-lead
**Tags**: epic=epic-prr-j, support, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

When agent confirms a dispute = real system error, one-click issue credit (e.g., free upload-quota bump, pro-rata refund) + auto-apology email in locale.

## Scope

- Agent action "confirm system error" → credit + email.
- Escalation path for recurring errors to engineering.
- Credit-ledger entry.

## Files

- Admin dashboard agent action.
- `src/backend/Cena.StudentApi/Controllers/DiagnosticCreditController.cs`
- Email templates HE/AR/EN.

## Definition of Done

- One-click resolve works.
- Credit reflected in user account.
- Apology email sent in correct locale.
- Full sln green.

## Non-negotiable references

- Memory "Labels match data" — credit matches promised remedy.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-390](TASK-PRR-390-support-audit-view.md), [PRR-392](TASK-PRR-392-disputed-diagnosis-taxonomy-feedback.md)
