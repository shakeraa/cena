# TASK-PRR-420: Per-student OCR-confidence tracking → typed-steps fallback trigger

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #8 accessibility
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, accessibility, ml, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Track rolling OCR confidence per student. If chronically low (e.g., median <0.70 across last 5 uploads), surface typed-steps alternative ([PRR-384](TASK-PRR-384-typed-steps-alternative.md)) as the default.

## Scope

- Rolling-window OCR confidence metric per student.
- Threshold config for "chronically low."
- UI nudge (not forced): "Want to type your steps?"
- Never framed as "your handwriting is unclear."
- Respects accessibility (memory "No stubs", persona #8).

## Files

- `src/backend/Cena.Diagnostic/Observability/StudentOcrConfidence.cs`
- Frontend nudge logic.
- Tests.

## Definition of Done

- Chronically-low triggers nudge.
- Nudge copy shipgate-passes.
- Full sln green.

## Non-negotiable references

- Memory "Ship-gate banned terms".
- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-384](TASK-PRR-384-typed-steps-alternative.md), [PRR-351](TASK-PRR-351-ocr-confidence-gate.md)
