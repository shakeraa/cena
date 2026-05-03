# TASK-PRR-424: Feature discovery UX — "upload photo" appears after wrong answer (TAIL)

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: tail — missing from original ladder
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + UX
**Tags**: epic=epic-prr-j, ux, priority=p0, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Students must know the feature exists at the moment it's useful. Surface "upload a photo of your work" CTA on the wrong-answer result screen (Plus + Premium tiers).

## Scope

- CTA appears on answer-incorrect states.
- Visible tier badge if Basic → prompts upgrade.
- Contextual copy: "Want me to see where you went wrong? Upload your work."
- Analytics: click-through rate per tier.

## Files

- `src/student/full-version/src/components/session/WrongAnswerActions.vue`
- Tests.

## Definition of Done

- CTA appears on wrong answers for Plus/Premium.
- Basic shows upgrade prompt.
- Shipgate passes.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "Ship-gate banned terms".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-380](TASK-PRR-380-diagnostic-result-screen.md), [EPIC-PRR-H §3.1](EPIC-PRR-H-student-input-modalities.md)
