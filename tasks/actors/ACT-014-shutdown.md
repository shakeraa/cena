# ACT-014: 5-Phase Graceful Shutdown Coordinator

**Priority:** P1 — blocks zero-data-loss deployment
**Blocked by:** ACT-001 (Cluster Bootstrap)
**Estimated effort:** 1 day
**Contract:** `contracts/actors/cluster_config.cs` (ACT-001.6 graceful shutdown)

---

## Context

ECS sends SIGTERM before killing a task. The shutdown coordinator must: (1) stop accepting new activations, (2) passivate active actors (flush state), (3) drain outbox entries to NATS, (4) leave the cluster, (5) close connections. All within 30 seconds (ECS `stopTimeout`).

## Subtasks

### ACT-014.1: 5-Phase Shutdown Implementation

**Files to create/modify:**
- `src/Cena.Actors.Host/ShutdownCoordinator.cs`
- `src/Cena.Actors.Host/Program.cs` — register IHostLifetime hook

**Acceptance:**
- [ ] Phase 1 (0-5s): Stop accepting new actor activations, return "shutting down" to new requests
- [ ] Phase 2 (5-15s): Passivate all active actors (persist state snapshots)
- [ ] Phase 3 (15-20s): Drain outbox (publish all pending NATS events)
- [ ] Phase 4 (20-25s): Leave cluster (deregister from DynamoDB)
- [ ] Phase 5 (25-30s): Close database connections, Redis connections, NATS connection
- [ ] If 30s exceeded: log WARNING, force exit
- [ ] No in-flight events lost: all events persisted before Phase 2 completes

**Test:**
```csharp
[Fact]
public async Task GracefulShutdown_CompletesAllPhases()
{
    var coordinator = new ShutdownCoordinator(_actorSystem, _outbox, _cluster);
    await coordinator.Shutdown(timeout: TimeSpan.FromSeconds(30));
    Assert.True(coordinator.Phase1Complete);
    Assert.True(coordinator.Phase2Complete);
    Assert.True(coordinator.Phase3Complete);
    Assert.True(coordinator.Phase4Complete);
    Assert.True(coordinator.Phase5Complete);
}

[Fact]
public async Task GracefulShutdown_PersistsInFlightEvents()
{
    var actor = await ActivateStudentActor("stu-1");
    await actor.Tell(new AttemptConcept { ConceptId = "math-1", IsCorrect = true });

    await _coordinator.Shutdown(timeout: TimeSpan.FromSeconds(30));

    // Verify event was persisted
    var events = await _eventStore.FetchStream("stu-1");
    Assert.NotEmpty(events);
}
```

---

### ACT-014.2: SIGTERM Handler + ECS Integration

**Files to create/modify:**
- `src/Cena.Actors.Host/Program.cs`

**Acceptance:**
- [ ] `SIGTERM` triggers `ShutdownCoordinator.Shutdown()`
- [ ] Health check returns 503 during shutdown (ECS stops routing traffic)
- [ ] ECS `stopTimeout: 30` matches shutdown coordinator timeout
- [ ] Docker `STOPSIGNAL SIGTERM` in Dockerfile

**Test:**
```bash
# Send SIGTERM to running container
docker kill --signal=SIGTERM cena-actors
# Assert: container exits within 30 seconds with code 0
docker logs cena-actors | grep "Shutdown complete"
```

---

### ACT-014.3: Shutdown Telemetry

**Files to create/modify:**
- `src/Cena.Actors.Host/ShutdownCoordinator.cs` (metrics)

**Acceptance:**
- [ ] Shutdown duration histogram: `cena.shutdown.duration_ms`
- [ ] Per-phase duration logged
- [ ] Actors passivated count logged
- [ ] Outbox entries drained count logged

**Test:**
```csharp
[Fact]
public async Task ShutdownMetrics_RecordDuration()
{
    await _coordinator.Shutdown(TimeSpan.FromSeconds(30));
    Assert.True(_metrics.ShutdownDurationMs > 0);
}
```

---

## Rollback Criteria
- If graceful shutdown fails, ECS force-kills after 30s; events in Marten are safe (already persisted), only outbox may have unpublished entries (caught up on next node startup)

## Definition of Done
- [ ] 5-phase shutdown completes within 30 seconds
- [ ] Zero data loss verified
- [ ] ECS integration tested
- [ ] PR reviewed by architect
