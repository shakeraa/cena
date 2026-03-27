# ACT-019: Decompose StudentActor — Extract Command Handlers

**Priority:** P1 — code quality, maintainability
**Blocked by:** None (all critical fixes applied)
**Estimated effort:** 2 days
**Source:** Actor system review H6 — file exceeds 500-line limit (currently 1282 lines)

---

## Context
`StudentActor.cs` is 1282 lines — far beyond the project's 500-line limit. It's the most critical actor in the system and the hardest to reason about. The inline BKT path (lines 370-484) duplicates logic from `BktService`. Multiple command handlers contain repetitive stage-flush-apply patterns.

## Subtasks

### ACT-019.1: Extract AttemptConceptHandler
**Files:**
- `src/actors/Cena.Actors/Students/Handlers/AttemptConceptHandler.cs` — new

**Acceptance:**
- [ ] `AttemptConceptHandler` class with `HandleAsync(IContext, AttemptConcept, StudentState)` method
- [ ] Remove inline BKT path — always delegate to `IBktService` via session actor or direct call
- [ ] Stage-flush-apply pattern encapsulated in handler
- [ ] StudentActor delegates to handler: `await _attemptHandler.HandleAsync(context, cmd, _state)`
- [ ] Unit test: handler produces correct events for correct/incorrect answers

### ACT-019.2: Extract SessionHandler
**Files:**
- `src/actors/Cena.Actors/Students/Handlers/SessionHandler.cs` — new

**Acceptance:**
- [ ] `SessionHandler` class managing `StartSession`, `EndSession` commands
- [ ] Child actor lifecycle (spawn/stop session actor) encapsulated
- [ ] Streak update logic moved here
- [ ] Unit test: session start/end produces correct events

### ACT-019.3: Extract StagnationHandler
**Files:**
- `src/actors/Cena.Actors/Students/Handlers/StagnationHandler.cs` — new

**Acceptance:**
- [ ] `StagnationHandler` class managing `StagnationDetected` internal message
- [ ] Methodology switch logic encapsulated
- [ ] Escalation NATS publish encapsulated
- [ ] Unit test: stagnation triggers correct methodology switch

### ACT-019.4: Slim Down StudentActor
**Files:**
- `src/actors/Cena.Actors/Students/StudentActor.cs` — modify

**Acceptance:**
- [ ] StudentActor < 500 lines
- [ ] `ReceiveAsync` only routes to handlers — no inline logic
- [ ] All handlers injected via constructor
- [ ] Lifecycle methods (OnStarted, OnStopping, etc.) remain in actor
- [ ] Build passes, existing tests pass
