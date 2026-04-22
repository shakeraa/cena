# TASK-PRR-381: Post-reflection narration + mastery signal

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #4 education research (productive-failure pattern)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + backend (mastery signal)
**Tags**: epic=epic-prr-j, ux, mastery, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

After reflection gate, two paths:
- Student retries and gets it right → celebration (small, not cartoonish) + mastery signal logged.
- Student still stuck → full misconception narration from template.

## Scope

- Retry path: student submits corrected step; CAS verifies; if correct, celebrate + emit mastery event.
- Full-narration path: render misconception-template explanation + example counter-case + suggested next step.
- No dark-pattern celebration (no streak/variable-reward mechanic — persona consensus + shipgate).
- Link into [EPIC-PRR-A StudentActor](EPIC-PRR-A-studentactor-decomposition.md) mastery engine.

## Files

- `src/student/full-version/src/components/diagnostic/ReflectionRetry.vue`
- `src/student/full-version/src/components/diagnostic/MisconceptionNarration.vue`
- Mastery-signal backend wire-up.

## Definition of Done

- Retry-success path logs mastery signal.
- Narration path renders template.
- Shipgate passes.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "Ship-gate banned terms" — no streaks.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-380](TASK-PRR-380-diagnostic-result-screen.md), [EPIC-PRR-A](EPIC-PRR-A-studentactor-decomposition.md)
