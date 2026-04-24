# TASK-PRR-425: Mobile camera affordances (angle + lighting guidance) (TAIL)

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: tail — improves OCR confidence at source
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend (PWA camera) + UX
**Tags**: epic=epic-prr-j, ux, ocr-quality, priority=p1, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Before capture, guide students to take a good photo: frame overlay, lighting-ok indicator, "hold steady" message. Raises OCR quality upstream and reduces downstream preview-edit friction.

## Scope

- PWA camera interface with frame overlay.
- Brightness heuristic → "a bit more light?" tip.
- Skew detection → auto-perspective-correct.
- Retake offered if auto-metric says poor.

## Files

- `src/student/full-version/src/components/diagnostic/CameraCapture.vue`
- Tests.

## Definition of Done

- Frame + lighting + skew guides work.
- Auto-perspective-correct applied.
- Accessibility preserved (non-camera path still available).

## Non-negotiable references

- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-351](TASK-PRR-351-ocr-confidence-gate.md), [EPIC-PRR-H §3.1](EPIC-PRR-H-student-input-modalities.md)
