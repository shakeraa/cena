# TASK-PRR-413: Face + name + school-logo redaction before OCR

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #5 compliance (incidental PII minimization)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + ML-engineer
**Tags**: epic=epic-prr-j, privacy, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Before the photo goes to the OCR vendor, detect + redact faces (if photo includes student's face), handwritten names in margins, school-logo / letterhead. Reduces incidental PII exposure to vendor.

## Scope

- Face detection (local model, not vendor-dependent).
- Handwritten-name heuristics (corner / margin text near top-right or top-left, especially if Hebrew / Arabic name pattern).
- Logo/letterhead detection (top-of-page banner regions).
- Redaction = solid-color blur overlay before vendor submission.
- Audit log of what was redacted.

## Files

- `src/backend/Cena.Diagnostic/Intake/PhotoRedactor.cs`
- Tests.

## Definition of Done

- Faces redacted when present.
- Top-margin name-patterns redacted.
- Logos/letterhead regions redacted.
- Math content preserved.

## Non-negotiable references

- PPL Amendment 13 (minimize biometric exposure).
- Israeli Privacy Law.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-410](TASK-PRR-410-ocr-vendor-dpa.md), [PRR-412](TASK-PRR-412-photo-deletion-sla.md)
