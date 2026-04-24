# 02 — Actor System Tasks

**Technology:** Proto.Actor .NET 9, Marten Event Sourcing, DynamoDB Cluster
**Contract files:** `contracts/actors/*.cs`, `contracts/backend/actor-contracts.cs`, `contracts/backend/domain-services.cs`
**Stage:** Foundation (Weeks 1-4) + Core Loop (Weeks 5-8)

---

## ACT-001: Proto.Actor Cluster Bootstrap
**Priority:** P0 — blocks all actor work
**Blocked by:** DATA-001
**Stage:** Foundation

**Description:**
Configure Proto.Actor cluster with DynamoDB cluster provider, gRPC transport, Protobuf serialization per `cluster_config.cs`.

**Acceptance Criteria:**
- [ ] Proto.Actor NuGet packages installed (Proto.Actor, Proto.Cluster, Proto.Persistence)
- [ ] DynamoDB cluster provider configured (table auto-created, 3s poll, 30s heartbeat)
- [ ] gRPC remote transport between nodes
- [ ] Protobuf serialization for all actor messages
- [ ] Health check endpoints: `/health/ready` and `/health/live`
- [ ] Cluster starts with 1 node locally, scales to 2-3 on ECS

**Test:**
```csharp
[Fact]
public async Task Cluster_StartsAndReportsHealthy()
{
    var system = ActorSystem.Create();
    // configure cluster
    await system.Cluster().StartMemberAsync();
    var health = await httpClient.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, health.StatusCode);
}
```

---

## ACT-002: StudentActor Skeleton (Virtual, Event-Sourced)
**Priority:** P0
**Blocked by:** ACT-001, DATA-001, DATA-002
**Stage:** Foundation

**Description:**
Implement the `StudentActor` virtual grain with event sourcing via Marten, per `student_actor.cs`.

**Acceptance Criteria:**
- [ ] Virtual actor: auto-activated on first message, passivated after 30min idle
- [ ] Event sourcing: state rebuilt from Marten event stream on activation
- [ ] Snapshot restoration: loads snapshot then replays events since snapshot
- [ ] `_state.EventVersion` tracked and passed as expected version to Marten
- [ ] `StageEvent()` + `FlushEvents()` batch pattern (atomic multi-event writes)
- [ ] Passivation persists any pending events and snapshot
- [ ] OpenTelemetry spans on all message handlers

**Test:**
```csharp
[Fact]
public async Task StudentActor_RehydratesFromSnapshot()
{
    // 1. Create actor, send 150 events (triggers snapshot at 100)
    // 2. Stop actor (passivation)
    // 3. Reactivate actor
    // 4. Assert state matches expected (snapshot + 50 replayed events)
    // 5. Assert _state.EventVersion == 150
}
```

---

## ACT-003: LearningSessionActor (Classic, Child)
**Priority:** P0
**Blocked by:** ACT-002
**Stage:** Core Loop

**Description:**
Implement the `LearningSessionActor` as a classic child of StudentActor, per `learning_session_actor.cs`.

**Acceptance Criteria:**
- [ ] Created by StudentActor on `StartSession`, destroyed on `EndSession`
- [ ] Inline BKT update (microsecond-scale, no I/O): `P(known|correct) = P(known)(1-P(slip)) / P(correct)`
- [ ] Fatigue score computed per question: `w1*accuracy_drop + w2*rt_increase + w3*time_fraction`
- [ ] Session ends when fatigue > 0.7 for 2 consecutive questions
- [ ] Item selection: zone-of-proximal-development priority (target P(known) ~ 0.5)
- [ ] 45-minute hard timeout
- [ ] All events delegated to parent StudentActor for persistence

**Test:**
```csharp
[Fact]
public void BktUpdate_IsCorrect()
{
    var result = BktService.Update(new BktUpdateInput(
        PriorMastery: 0.5, IsCorrect: true,
        Params: new BktParameters("c1", PLearning: 0.1, PSlip: 0.1, PGuess: 0.25)));
    Assert.True(result.PosteriorMastery > 0.5);
    Assert.True(result.PosteriorMastery < 1.0);
}

[Fact]
public void FatigueScore_TriggersSessionEnd()
{
    // Simulate 5 questions with declining accuracy and increasing RT
    // Assert fatigue > 0.7 after question 4
    // Assert session recommends end
}
```

---

## ACT-004: StagnationDetectorActor
**Priority:** P1
**Blocked by:** ACT-002
**Stage:** Intelligence (Weeks 9-12)

**Description:**
Implement per `stagnation_detector_actor.cs` with adaptive thresholds.

**Acceptance Criteria:**
- [ ] 5-signal composite score with normalization formulas (sigmoid, linear)
- [ ] Per-student adaptive threshold: `max(0.02, avg_improvement_rate * 0.5)`
- [ ] Default weights: accuracy=0.30, RT=0.20, abandonment=0.20, error=0.20, sentiment=0.10
- [ ] Triggers `StagnationDetected` when score > 0.7 for 3 consecutive sessions
- [ ] 3-session cooldown after methodology switch (resets stagnation tracking)
- [ ] State persists across sessions (NOT lost on parent passivation)

**Test:**
```csharp
[Fact]
public void StagnationDetector_TriggersAfter3Sessions()
{
    var detector = new StagnationDetectorActor(...);
    // Feed 3 sessions with flat accuracy (0% improvement)
    // Assert StagnationDetected emitted
    // Feed 1 more session after methodology switch
    // Assert cooldown prevents re-trigger
}

[Fact]
public void AdaptiveThreshold_PreventsSlowLearnerFalsePositive()
{
    // Student with historical 2% improvement rate
    // Gets adaptive threshold = max(0.02, 0.02 * 0.5) = 0.02
    // 1.5% improvement should NOT trigger stagnation (below adaptive threshold)
}
```

---

## ACT-005: OutreachSchedulerActor
**Priority:** P2
**Blocked by:** ACT-002
**Stage:** Core Loop

**Description:**
Implement HLR timer management per `outreach_scheduler_actor.cs`.

**Acceptance Criteria:**
- [ ] HLR formula: `p(t) = 2^(-delta/h)` — schedules review when recall < 0.85
- [ ] Priority ordering: 1=StreakExpiring, 2=ReviewDue, 3=StagnationDetected
- [ ] Throttling: max 3 messages/day, quiet hours 22:00-07:00 Israel time
- [ ] Channel routing: WhatsApp > Push > Telegram (per student preference)
- [ ] Publishes to NATS `cena.outreach.commands.*` for dispatch
- [ ] Timers survive passivation (rebuild from state on reactivation)

**Test:**
```csharp
[Fact]
public void HlrTimer_SchedulesReviewWhenRecallDrops()
{
    // Set half-life = 24 hours, last review = 48 hours ago
    // p(48) = 2^(-48/24) = 0.25 → well below 0.85
    // Assert ReviewDue event emitted
}

[Fact]
public void Throttling_EnforcesMaxThreePerDay()
{
    // Send 5 outreach triggers
    // Assert only 3 are dispatched, 2 are queued for tomorrow
}
```

---

## ACT-006: MethodologySwitchService
**Priority:** P1
**Blocked by:** DATA-006 (Neo4j), ACT-004
**Stage:** Intelligence

**Description:**
Implement the 5-step MCM lookup algorithm per `methodology_switch_service.cs`.

**Acceptance Criteria:**
- [ ] Step 1: Classify dominant error type (precedence: conceptual > procedural > motivational)
- [ ] Step 2: MCM graph lookup from Neo4j cache: `(error_type, concept_category) → candidates`
- [ ] Step 3: Filter out methods in `MethodAttemptHistory` for this concept cluster
- [ ] Step 4: Select first candidate with confidence > 0.5; else best available
- [ ] Step 5: Fallback to error-type defaults if no MCM entry
- [ ] Cycling prevention: track last 3 cycles per concept
- [ ] Escalation: all 8 methods exhausted → "mentor-resistant" flag

**Test:**
```csharp
[Fact]
public async Task McmLookup_ReturnsCorrectMethodology()
{
    // Set up MCM: (conceptual, algebra) → [(socratic, 0.85), (feynman, 0.70)]
    // Already tried: socratic
    // Expected result: feynman (0.70)
}

[Fact]
public async Task Escalation_WhenAllMethodsExhausted()
{
    // Try all 8 methodologies
    // Assert decision.IsEscalation == true
    // Assert decision.SuggestedAction contains "human tutor"
}
```

---

## ACT-007: PrerequisiteEnforcementService
**Priority:** P1
**Blocked by:** DATA-006 (Neo4j)
**Stage:** Core Loop

**Description:**
Implement prerequisite gate checks per `domain-services.cs` `IPrerequisiteEnforcementService`.

**Acceptance Criteria:**
- [ ] `CheckPrerequisites` returns `IsUnlocked=false` when ANY prerequisite has mastery < 0.95
- [ ] Uses `PrerequisiteGateThreshold = 0.95` (NOT `ProgressionThreshold = 0.85`)
- [ ] `GetUnlockedFrontier` returns concepts where ALL prerequisites are satisfied
- [ ] `GetBlockedConcepts` returns concepts with unmet prerequisites + gap amounts
- [ ] Caches prerequisite graph from Neo4j (in-memory, refreshed on CurriculumPublished)

**Test:**
```csharp
[Fact]
public async Task PrerequisiteGate_BlocksAtLowMastery()
{
    // Concept "calculus" requires "algebra" at 0.95
    // Student has algebra mastery = 0.88
    // Assert CheckPrerequisites("calculus") → IsUnlocked = false
    // Assert MissingPrerequisites = ["algebra"]
    // Assert PrerequisiteMasteryGaps = { "algebra": 0.07 }
}

[Fact]
public async Task Frontier_OnlyIncludesUnlockedConcepts()
{
    // 3 concepts: A (no prereqs), B (requires A at 0.95), C (requires B at 0.95)
    // Student mastery: A=0.96, B=0.40, C=0.00
    // Frontier = ["B"] (A is mastered, C is blocked by B)
}
```

---

## ACT-008: CurriculumGraphActor
**Priority:** P1
**Blocked by:** ACT-001, DATA-006
**Stage:** Foundation

**Description:**
Implement in-memory curriculum graph actor per `actor_system_topology.cs`.

**Acceptance Criteria:**
- [ ] Singleton actor, loads full graph from Neo4j on startup
- [ ] Microsecond-latency lookups: `GetConcept`, `GetPrerequisites`, `GetFrontier`
- [ ] Spawns `McmGraphActor` as child
- [ ] Hot-reloads on `CurriculumPublished` NATS event
- [ ] Reports health: concept count, edge count, version, loaded-at timestamp

**Test:**
```csharp
[Fact]
public async Task CurriculumGraph_LoadsAndAnswersQueries()
{
    // Load 5 sample concepts
    var concept = await actor.RequestAsync<ConceptNode>(new GetConceptQuery("math-algebra-1"));
    Assert.NotNull(concept);
    Assert.Equal("algebra", concept.Category);
}
```

---

## ACT-009: LlmGatewayActor + Circuit Breakers
**Priority:** P1
**Blocked by:** ACT-001
**Stage:** Foundation

**Description:**
Implement per-model circuit breakers per `actor_system_topology.cs`.

**Acceptance Criteria:**
- [ ] LlmGatewayActor routes requests to Kimi/Sonnet/Opus circuit breaker children
- [ ] Each breaker: Closed → Open (N failures) → HalfOpen (after timeout) → Closed (3 successes)
- [ ] Kimi: 5 failures / 60s open. Sonnet: 3 failures / 90s. Opus: 2 failures / 120s
- [ ] Open circuit returns `LlmCircuitOpenResponse` (caller uses fallback)
- [ ] Metrics: `cena.llm.circuit_opened`, `cena.llm.circuit_rejected`

**Test:**
```csharp
[Fact]
public async Task CircuitBreaker_OpensAfterThresholdAndRecovers()
{
    // Send 5 failures to Kimi breaker
    // Assert state = Open
    // Wait 60s
    // Send 1 success → state = HalfOpen
    // Send 2 more successes → state = Closed
}
```

---

## ACT-010: StudentActorManager (Pool Governor)
**Priority:** P2
**Blocked by:** ACT-002
**Stage:** Core Loop

**Description:**
Implement activation budget and back-pressure per `actor_system_topology.cs`.

**Acceptance Criteria:**
- [ ] Max 10,000 concurrent actors (configurable)
- [ ] Back-pressure queue: reject at depth > 1,000
- [ ] `DrainAllStudents` for graceful shutdown
- [ ] Metrics: `cena.actors.student_active`, `cena.actors.activation_queue`
- [ ] Rate limiter: max 200 activations/second (prevents Bagrut night thundering herd)

**Test:**
```csharp
[Fact]
public void BackPressure_RejectsAtQueueLimit()
{
    // Fill pool to max (10,000)
    // Queue 1,001 activations
    // Assert 1001st returns ActivationResult(Success=false, Error="Back-pressure")
}
```

---

## ACT-011: Offline Sync Handler (Idempotent)
**Priority:** P0
**Blocked by:** ACT-002, DATA-007 (Redis)
**Stage:** Core Loop

**Description:**
Implement `HandleSyncOfflineEvents` with idempotency per fixed `student_actor.cs`.

**Acceptance Criteria:**
- [ ] Each offline event checked against Redis `SET NX` before processing
- [ ] Duplicate events are skipped (logged, not errored)
- [ ] Events processed in chronological order (client timestamp, adjusted for clock skew)
- [ ] Three-tier classification: Unconditional / Conditional / ServerAuthoritative
- [ ] Conditional events: full weight (1.0) or reduced (0.75) based on context validity
- [ ] All events committed atomically via `FlushEvents()` (not one-by-one)

**Test:**
```csharp
[Fact]
public async Task OfflineSync_SkipsDuplicates()
{
    var events = GenerateOfflineEvents(5);
    await actor.Tell(new SyncOfflineEvents(events));
    await actor.Tell(new SyncOfflineEvents(events)); // retry
    // Assert only 5 events in Marten stream (not 10)
}
```

---

## ACT-012: NATS Outbox Publisher
**Priority:** P1
**Blocked by:** ACT-002, DATA-001
**Stage:** Core Loop

**Description:**
Build background service that catches up on NATS publishes missed during failures.

**Acceptance Criteria:**
- [ ] Background service polls Marten for events not yet confirmed in NATS
- [ ] Uses `nats_published_at` metadata column (nullable timestamp)
- [ ] Stamps `nats_published_at` on successful NATS publish
- [ ] Handles NATS downtime: queues up, publishes on reconnect
- [ ] Runs every 5 seconds, processes max 100 events per cycle

**Test:**
```csharp
[Fact]
public async Task OutboxPublisher_CatchesUpMissedEvents()
{
    // 1. Persist 10 events with NATS down (nats_published_at = null)
    // 2. Start outbox publisher
    // 3. Restore NATS
    // 4. Wait for publisher cycle
    // 5. Assert all 10 events have nats_published_at set
    // 6. Assert NATS consumers received all 10
}
```

---

## ACT-013: Supervision Strategies
**Priority:** P2
**Blocked by:** ACT-002
**Stage:** Core Loop

**Description:**
Implement all supervision strategies per `supervision_strategies.cs`.

**Acceptance Criteria:**
- [ ] Root: OneForOne, restart with exponential backoff (1s→30s cap)
- [ ] Student children: 3 failures in 60s → stop child
- [ ] LLM Gateway: Resume (circuit breakers self-manage)
- [ ] Outreach workers: restart, escalate if pool exhausted
- [ ] Poison message detection: quarantine after 2 consecutive failures on same message type
- [ ] Dead letter watcher: logs, counts, alerts at threshold

**Test:**
```csharp
[Fact]
public async Task Supervision_RestartsChildOnFailure()
{
    // Cause LearningSessionActor to throw
    // Assert actor is restarted (new instance, same PID)
    // Cause 3 failures in 60s
    // Assert actor is stopped (not restarted)
}
```

---

## ACT-014: GracefulShutdownCoordinator
**Priority:** P2
**Blocked by:** ACT-010
**Stage:** Core Loop

**Description:**
Implement 5-phase drain per `actor_system_topology.cs`.

**Acceptance Criteria:**
- [ ] Phase 1: Stop new activations
- [ ] Phase 2: Wait for active sessions to end (max 30s)
- [ ] Phase 3: Flush analytics buffer to S3
- [ ] Phase 4: Close LLM connections
- [ ] Phase 5: Leave cluster gracefully
- [ ] No data loss during rolling deployments
- [ ] Completes within 30 seconds under normal load

**Test:**
```csharp
[Fact]
public async Task GracefulShutdown_CompletesWithin30Seconds()
{
    // Start 100 actors with active sessions
    // Initiate shutdown
    // Assert all actors passivated
    // Assert analytics flushed
    // Assert shutdown < 30s
}
```

---

## Architect Review Tasks (2026-03-27) — COMPLETED

| Task | Priority | Summary | Status |
|------|----------|---------|--------|
| [ACT-026](actors/done/ACT-026-snapshot-noop-fix.md) | P0 | ForceSnapshot is a no-op — enable Marten inline snapshots | DONE |
| [ACT-027](actors/done/ACT-027-duplicate-nats-publish.md) | P0 | Remove duplicate NATS publishing — keep outbox only | DONE |
| [ACT-028](actors/done/ACT-028-methodology-switch-timestamp.md) | P0 | MethodologySwitched_V1 uses wall clock in Apply — non-deterministic replay | DONE |
| [ACT-029](actors/done/ACT-029-delegate-event-flush.md) | P1 | DelegateEvent staged but never flushed — events lost | DONE |
| [ACT-030](actors/done/ACT-030-double-session-started.md) | P1 | Double SessionStarted events when child actors are wired | DONE |
| [ACT-031](actors/done/ACT-031-telemetry-static-meter-cleanup.md) | P2 | Migrate remaining static Meters to IMeterFactory | DONE |
| [ACT-032](actors/done/ACT-032-dead-code-and-wiring.md) | P2 | Dead code cleanup, DI wiring fixes, timezone alignment | DONE |
