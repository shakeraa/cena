# ACT-030: Double SessionStarted Events When Child Actors Are Wired

**Priority:** P1 — HIGH (latent bug, triggers when child actors are enabled)
**Blocked by:** None (but related to child actor wiring)
**Estimated effort:** 0.5 days
**Source:** Architect review 2026-03-27, Issue #6

---

## Problem

When `_sessionActor` is null (current state — `SpawnChildActors` is a no-op), `HandleStartSession` in `StudentActor.Commands.cs` creates and flushes `SessionStarted_V1` directly.

When `_sessionActor` is non-null (future state), `HandleInit` in `LearningSessionActor` **also** sends a `DelegateEvent(SessionStarted_V1)` to the parent via `context.Send(context.Parent, ...)`.

If child actors are ever wired up, you'll get **double session-started events** and double `SessionCount` increments.

Similarly, `ConceptAttempted_V1` is emitted both inline in the `_sessionActor == null` path AND delegated from `LearningSessionActor.HandleEvaluateAnswer`.

## Files

- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` — `HandleStartSession` (lines 244–295), `HandleAttemptConcept` (lines 36–239)
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` — `HandleInit` (lines 83–104), `HandleEvaluateAnswer` (lines 107–179)

## Subtasks

### ACT-030.1: Remove event emission from LearningSessionActor.HandleInit
- [ ] `LearningSessionActor.HandleInit` should NOT delegate `SessionStarted_V1` — the parent already emits it
- [ ] The session actor should only delegate events it owns (ConceptAttempted, HintRequested, QuestionSkipped, SessionEnded)

### ACT-030.2: Clarify ownership of ConceptAttempted emission
- [ ] When `_sessionActor` is available, the parent should NOT emit `ConceptAttempted_V1` inline
- [ ] The session actor delegates `ConceptAttempted_V1` via `DelegateEvent` — that's the single source
- [ ] The parent's inline BKT path (lines 71–151) is the fallback when session actor is null

### ACT-030.3: Add guard comments
- [ ] Document in both files which actor owns which event emission
- [ ] Add a `Debug.Assert` or log warning if both paths somehow execute

## Acceptance Criteria

- [ ] Each session start produces exactly one `SessionStarted_V1` event
- [ ] Each attempt produces exactly one `ConceptAttempted_V1` event
- [ ] Works correctly in both modes: with and without session actor
- [ ] Build and tests pass
