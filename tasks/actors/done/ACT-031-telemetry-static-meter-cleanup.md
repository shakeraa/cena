# ACT-031: Migrate Remaining Static Meters to IMeterFactory

**Priority:** P2 — MEDIUM (test isolation, metrics hygiene)
**Blocked by:** None
**Estimated effort:** 1 day
**Source:** Architect review 2026-03-27, Issue #7

---

## Problem

ACT-023 migrated `StudentActor`, `LearningSessionActor`, `StagnationDetectorActor`, and `OutreachSchedulerActor` to instance-based `IMeterFactory`. But 6 components still use `static Meter`:

1. `LlmCircuitBreakerActor` — lines 117–121
2. `CurriculumGraphActor` — line 194
3. `DeadLetterWatcher` — line 71
4. `NatsOutboxPublisher` — line 72
5. `DecayPropagationService` — lines 42–43
6. `FocusDegradationService` — lines 79–81

Static meters don't respond to `IMeterFactory` configuration, breaking test isolation and scoped metric listeners.

## Files

- `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs`
- `src/actors/Cena.Actors/Graph/CurriculumGraphActor.cs`
- `src/actors/Cena.Actors/Infrastructure/DeadLetterWatcher.cs`
- `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs`
- `src/actors/Cena.Actors/Services/DecayPropagationService.cs`
- `src/actors/Cena.Actors/Services/FocusDegradationService.cs`

## Subtasks

### ACT-031.1: Inject IMeterFactory into each component
- [ ] Add `IMeterFactory` parameter to each constructor
- [ ] Replace `static Meter` with instance field: `_meter = meterFactory.Create("Cena.Actors.X", "1.0.0")`
- [ ] Replace `static Counter/Histogram` with instance fields created from `_meter`

### ACT-031.2: Update DI registrations and actor spawning
- [ ] Ensure `IMeterFactory` is available via DI (it is by default in .NET 8+)
- [ ] Update `LlmGatewayActor` to pass `IMeterFactory` when spawning `LlmCircuitBreakerActor`
- [ ] Update `ActorSystemManager` to pass through to child actors

### ACT-031.3: Also migrate static ActivitySource instances
- [ ] `CurriculumGraphActor` line 193: `static ActivitySource`
- [ ] `NatsOutboxPublisher` line 71: `static ActivitySource`
- [ ] These should be instance fields (ActivitySource doesn't have a factory pattern, but should still be non-static for consistency)

## Acceptance Criteria

- [ ] No `static Meter` or `static Counter/Histogram` remains in the codebase
- [ ] All metrics use `IMeterFactory` for creation
- [ ] Build and tests pass
