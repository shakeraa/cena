# TASK-PRR-351: OCR-confidence gate → preview UX routing

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #1 student (silent mis-OCR kills trust), #8 accessibility
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, priority=p0, trust, accessibility
**Status**: Ready (requires PRR-350 types)
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Route extracted step sequences below a confidence threshold to the editable preview UX ([PRR-352](TASK-PRR-352-editable-preview-ux.md)) instead of straight to CAS verification. Never commit to a silent mis-OCR.

## Scope

- Per-step confidence threshold (configurable; start at 0.80).
- Any step <threshold → route entire sequence to preview.
- Aggregate confidence metric for sequence-level flag.
- Log confidence distribution for tuning.

## Files

- `src/backend/Cena.Diagnostic/StepExtraction/ConfidenceGate.cs`
- Config for threshold.
- Tests.

## Definition of Done

- Low-confidence step routes to preview.
- High-confidence sequence goes straight to CAS.
- Threshold configurable without deploy.

## Non-negotiable references

- Memory "Labels match data" — never claim to have read something we didn't.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-352](TASK-PRR-352-editable-preview-ux.md), [PRR-350](TASK-PRR-350-step-extraction-service.md), [PRR-420](TASK-PRR-420-per-student-ocr-confidence-tracking.md)
