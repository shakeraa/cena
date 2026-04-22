# TASK-PRR-380: Diagnostic result screen v1 (first-wrong-step + reflection gate)

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #1 student, #4 education research (reflection gate mandatory)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + UX
**Tags**: epic=epic-prr-j, ux, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Primary diagnostic result UX: surface the first wrong step with reflection-gate copy ("I see the error. Try again — hint available") BEFORE full narration unlocks. Turns feature from answer-checker into learning scaffold.

## Scope

- Layout: student's step sequence displayed, first-wrong-step highlighted, reflection-gate copy prominent.
- "Try again" button → primary.
- "Show me the hint" → secondary (opens [PRR-381](TASK-PRR-381-post-reflection-narration.md) narration).
- HE/AR/EN copy, shipgate-compliant (no negative framing on the student).
- Analytics events for reflection-gate interaction.

## Files

- `src/student/full-version/src/components/diagnostic/DiagnosticResult.vue`
- `src/student/full-version/src/composables/useDiagnosticResult.ts`
- Tests.

## Definition of Done

- Reflection-gate appears before narration.
- Both paths (try-again / show-hint) work.
- Copy passes shipgate.
- Full sln green.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "Honest not complimentary" — accurate but encouraging.
- Memory "Ship-gate banned terms".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-381](TASK-PRR-381-post-reflection-narration.md), [PRR-382](TASK-PRR-382-show-my-work-view.md)
