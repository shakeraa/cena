# TASK-PRR-384: Typed-steps alternative UX (accessibility-first fallback)

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #8 accessibility (dysgraphia-friendly), #1 student (never blame handwriting)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + accessibility lead
**Tags**: epic=epic-prr-j, accessibility, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Alternative: student types their work in MathLive step-by-step instead of uploading a photo. Surfaced by default when a student's OCR confidence is consistently low ([PRR-420](TASK-PRR-420-per-student-ocr-confidence-tracking.md)).

## Scope

- "Type your steps" mode uses MathLive (reuse EPIC-PRR-H primitives).
- Warm copy: "Want to type your steps instead? Often faster" — NOT "your handwriting is unclear."
- Submits structured step sequence directly to CAS (skips OCR entirely).
- Per-student default-mode preference setting.

## Files

- `src/student/full-version/src/components/diagnostic/TypedStepsInput.vue`
- Tests.

## Definition of Done

- Typed mode end-to-end to CAS works.
- Default-mode setting persists.
- Copy positive framing, shipgate passes.

## Non-negotiable references

- Memory "Ship-gate banned terms".
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-420](TASK-PRR-420-per-student-ocr-confidence-tracking.md), [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md) MathLive
