# ACT-010: Student Pool Governor — Back-Pressure, Activation Rate Limiter

**Priority:** P1 — blocks production capacity planning
**Blocked by:** ACT-001 (Cluster Bootstrap)
**Estimated effort:** 2 days
**Contract:** `contracts/actors/student_actor.cs` (memory budget ~500KB per actor)

---

## Context

Each StudentActor consumes ~500KB. With 10,000 concurrent students, that is ~5GB memory. The pool governor limits concurrent active actors, applies back-pressure when memory exceeds 80%, and rate-limits new activations to prevent thundering herd on server restart.

## Subtasks

### ACT-010.1: Pool Governor Actor

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/PoolGovernorActor.cs`

**Acceptance:**
- [ ] Tracks active actor count via cluster membership events
- [ ] Hard limit: configurable max actors per node (default: 5000)
- [ ] Soft limit: 80% of hard limit triggers proactive passivation of idle actors (>30 min inactive)
- [ ] Activation rejected when hard limit reached -> client gets "server busy, retry in 30s"

**Test:**
```csharp
[Fact]
public async Task PoolGovernor_RejectsActivationAtLimit()
{
    var governor = CreateGovernor(maxActors: 3);
    for (int i = 0; i < 3; i++)
        await governor.Tell(new ActorActivated($"stu-{i}"));
    var result = await governor.Ask<ActivationResult>(new RequestActivation("stu-4"));
    Assert.False(result.Allowed);
}
```

---

### ACT-010.2: Back-Pressure + Memory Monitoring

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/MemoryPressureMonitor.cs`

**Acceptance:**
- [ ] Polls process memory every 30 seconds
- [ ] Memory > 80% of node limit -> passivate oldest idle actors
- [ ] Memory > 90% -> emergency: passivate all actors with no active session
- [ ] Metrics: `cena.pool.active_actors`, `cena.pool.memory_mb`, `cena.pool.passivations_total`

**Test:**
```csharp
[Fact]
public async Task BackPressure_PassivatesIdleActorsAtThreshold()
{
    var monitor = CreateMonitor(memoryLimitMb: 100, currentMemoryMb: 85);
    var passivated = await monitor.ApplyPressure();
    Assert.True(passivated > 0);
}
```

---

### ACT-010.3: Activation Rate Limiter

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/ActivationRateLimiter.cs`

**Acceptance:**
- [ ] Max 100 activations per second per node (prevents thundering herd on restart)
- [ ] Token bucket algorithm with 100 tokens, refill rate 100/s
- [ ] Overflow: queue up to 500 pending activations, then reject
- [ ] On cold start: gradual ramp-up over 60 seconds (start at 10/s, increase to 100/s)

**Test:**
```csharp
[Fact]
public async Task RateLimiter_ThrottlesActivations()
{
    var limiter = new ActivationRateLimiter(maxPerSecond: 10);
    int allowed = 0;
    for (int i = 0; i < 50; i++)
        if (limiter.TryAcquire()) allowed++;
    Assert.True(allowed <= 15); // Some burst allowed
}
```

---

## Rollback Criteria
- Disable pool governor; rely on ECS auto-scaling for capacity management (higher cost)

## Definition of Done
- [ ] Pool governor limits active actors
- [ ] Back-pressure passivates idle actors at memory threshold
- [ ] Activation rate limiter prevents thundering herd
- [ ] PR reviewed by architect
