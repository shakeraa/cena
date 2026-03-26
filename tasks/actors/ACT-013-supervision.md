# ACT-013: Supervision Strategies Per Actor Type

**Priority:** P1 — blocks production reliability
**Blocked by:** ACT-001 (Cluster Bootstrap)
**Estimated effort:** 1 day
**Contract:** `contracts/actors/supervision_strategies.cs` (CenaSupervisionStrategies)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Configures the supervision tree: root strategy with exponential backoff for StudentActor grains, OneForOne child strategy for session/stagnation/outreach actors (3 failures in 60s -> stop), poison message quarantine, and dead letter handling.

## Subtasks

### ACT-013.1: Root + Child Strategy Registration

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/SupervisionConfiguration.cs`
- `src/Cena.Actors.Host/Program.cs`

**Acceptance:**
- [ ] Root strategy: `ExponentialBackoffStrategy(1s initial, 30s max, reset after 2 min)`
- [ ] Child strategy: `OneForOneStrategy(3 retries in 60s)`, then Stop
- [ ] Poison message wrapper on child strategy
- [ ] Dead letter handler configured on ActorSystem EventStream
- [ ] All strategies log at WARNING (restart) or ERROR (stop) with structured context

**Test:**
```csharp
[Fact]
public async Task ChildStrategy_StopsAfterThreeFailures()
{
    var childProps = Props.FromFunc(ctx => throw new Exception("crash"));
    var parent = CreateParentWithChildStrategy();
    var child = parent.SpawnChild(childProps);

    for (int i = 0; i < 4; i++)
        parent.Send(child, "trigger-crash");

    await Task.Delay(2000);
    Assert.True(await IsActorStopped(child));
}
```

---

### ACT-013.2: Dead Letter Monitoring

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/DeadLetterMonitor.cs`

**Acceptance:**
- [ ] Dead letters logged with message type, target PID, sender PID
- [ ] Counter: `cena.supervision.dead_letters_total`
- [ ] Alert: dead letter rate > 100/min -> WARNING
- [ ] Dead letter spike often indicates passivation/reactivation timing issue

**Test:**
```csharp
[Fact]
public void DeadLetterHandler_LogsAndCounts()
{
    var system = CreateActorSystem();
    CenaSupervisionStrategies.ConfigureDeadLetterHandling(system, _logger);
    system.Root.Send(PID.FromAddress("nonexistent", "nowhere"), "orphaned-message");
    Assert.True(_logger.ContainsWarning("Dead letter"));
}
```

---

## Rollback Criteria
- Use default Proto.Actor supervision (restart always, no backoff)

## Definition of Done
- [ ] Supervision tree configured per contract
- [ ] Dead letter monitoring active
- [ ] PR reviewed by architect
