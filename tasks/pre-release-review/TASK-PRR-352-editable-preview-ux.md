# TASK-PRR-352: Editable preview UX — "is this what you wrote?"

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #1 student (trust), #8 accessibility
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + UX
**Tags**: epic=epic-prr-j, ux, priority=p0, trust
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

When extraction confidence is low OR student requests, show them what the system thinks they wrote — editable — before running CAS analysis.

## Scope

- Preview screen: rendered LaTeX of each step, editable via MathLive (reuse EPIC-PRR-H MathLive integration).
- Warm copy: "let me show you what I see — edit anything I got wrong" (NOT "your handwriting is unclear").
- Submit → CAS analysis on confirmed sequence.
- Cancel / redo photo option.
- HE/AR/EN copy.

## Files

- `src/student/full-version/src/components/diagnostic/ExtractedPreview.vue`
- Integration with MathLive editor.
- Tests: edit flows, locale, submit → CAS.

## Definition of Done

- Low-confidence sequences route here.
- Edits reflected in submitted sequence.
- Copy passes shipgate (no negative framing).
- Full sln green.

## Non-negotiable references

- Memory "Ship-gate banned terms".
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-350](TASK-PRR-350-step-extraction-service.md), [PRR-351](TASK-PRR-351-ocr-confidence-gate.md), [PRR-384](TASK-PRR-384-typed-steps-alternative.md)
