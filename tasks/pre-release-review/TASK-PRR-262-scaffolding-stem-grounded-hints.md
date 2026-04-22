# TASK-PRR-262: Scaffolding stem-grounded hint variant for hidden-options mode

**Priority**: P1 — persona-educator correctness concern
**Effort**: S-M (1 week)
**Lens consensus**: persona-educator, persona-cogsci
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3.4](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md)
**Assignee hint**: kimi-coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p1, scaffolding, pedagogy, q2
**Status**: Blocked on [PRR-260](TASK-PRR-260-hide-reveal-session-toggle.md) landing first
**Source**: 10-persona 002-brief review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

When a student is in `attemptMode=hidden_reveal` or `revealState=classroom_enforced` and requests a scaffolding hint, the `ScaffoldingService` must return a hint that **does not leak the options**. Otherwise the generation-effect pedagogy is destroyed by the scaffolding ladder.

## Scope

- `ScaffoldingService` gains `GetHint(sessionId, questionId, attemptMode)`.
- Hint bank per question carries two variants: `stem-grounded` (no option references) and `full` (may reference option shapes).
- When mode is hidden: only `stem-grounded` hints are eligible.
- If no `stem-grounded` hint exists for the question's ladder level: return an honest "No hint available at this level for self-test mode — click to reveal options first" prompt. Do NOT silently serve a full-variant hint.
- Extend item-authoring pipeline (per-question): `authored_hints[]` carries `variant` field. Authoring UI forces at least L1 + L2 `stem-grounded` coverage for questions that can be entered with hidden options (a prerequisite flag on the question).

## Non-leaking test (persona-educator)

- Automated: sample 100 hints per subject; grep for option-letter patterns (`option A`, `answer A`, `אפשרות א`, `الخيار أ`) + option-content echoes (stem-to-hint text overlap). Hints that match flag for author review.
- Reject any `stem-grounded` hint that fails this test.

## Files

- `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` — extend with mode-aware routing.
- `src/actors/Cena.Actors/Mastery/ScaffoldingLevel.cs` — add variant dimension.
- Item-bank schema extension: per-hint `variant` field.
- Item-authoring admin UI — variant input.
- Tests: stem-grounded-only returned when hidden; "no hint available" copy when bank is empty; leak-detection on sample.

## Definition of Done

- No option-leaking hint reaches a student in hidden mode across test corpus.
- Authoring UI enforces variant coverage for hide-reveal-eligible items.
- `ScaffoldingService` test coverage extended.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md).
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + leak-test report>"`

## Related

- PRR-260, PRR-261.
- Existing `ScaffoldingService` at `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs`.
