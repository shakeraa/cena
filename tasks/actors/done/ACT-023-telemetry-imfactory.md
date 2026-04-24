# ACT-023: Migrate Telemetry to IMeterFactory for Testability

**Priority:** P3 — best practices / testability
**Blocked by:** None
**Estimated effort:** 1 day
**Source:** Actor system review M2 — static Meter/ActivitySource instances without Dispose

---

## Context
Every actor and service creates `static readonly Meter` and `static readonly ActivitySource` instances. While functional, this pattern prevents unit tests from verifying telemetry output and makes it impossible to scope metrics to a specific actor system instance in multi-tenant scenarios. OpenTelemetry .NET recommends `IMeterFactory` from DI.

## Subtasks

### ACT-023.1: Inject IMeterFactory into Actors
**Files (all actors):**
- `StudentActor.cs`, `LearningSessionActor.cs`, `StagnationDetectorActor.cs`
- `OutreachSchedulerActor.cs`, `McmGraphActor.cs`, `CurriculumGraphActor.cs`
- `LlmCircuitBreakerActor.cs`, `StudentActorManager.cs`, `ActorSystemManager.cs`
- `DeadLetterWatcher.cs`

**Acceptance:**
- [ ] Replace `static readonly Meter` with instance field created via `IMeterFactory.Create("Cena.Actors.X")`
- [ ] Replace `static readonly ActivitySource` with instance field
- [ ] All actors accept `IMeterFactory` via constructor injection
- [ ] Register `IMeterFactory` in DI (already provided by `OpenTelemetry.Extensions.Hosting`)

### ACT-023.2: Inject into Services
**Files:**
- `BktService.cs` (no telemetry currently)
- `CognitiveLoadService.cs`, `FocusDegradationService.cs`

**Acceptance:**
- [ ] Services that emit metrics accept `IMeterFactory`
- [ ] Services without telemetry — no change needed

### ACT-023.3: Test Telemetry
**Acceptance:**
- [ ] Unit test: verify `cena.student.attempts_total` counter increments on AttemptConcept
- [ ] Unit test: verify `cena.session.fatigue_score` histogram records on answer evaluation
