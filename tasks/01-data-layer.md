# 01 — Data Layer Tasks

**Technology:** PostgreSQL 16, Marten v7.x, Neo4j, Redis (ElastiCache), S3
**Contract files:** `contracts/data/marten-event-store.cs`, `contracts/data/neo4j-schema.cypher`, `contracts/data/redis-contracts.ts`, `contracts/data/s3-export-schema.json`
**Stage:** Foundation (Weeks 1-4)

---

## DATA-001: PostgreSQL + Marten Event Store Setup
**Priority:** P0 — blocks all backend work
**Blocked by:** None

**Description:**
Provision PostgreSQL 16 (RDS or local Docker), configure Marten v7.x event store with the schema from `marten-event-store.cs`.

**Acceptance Criteria:**
- [ ] PostgreSQL 16 running with `cena` database and `cena` schema
- [ ] Marten v7.x NuGet installed, `ConfigureCenaEventStore()` method compiles and runs
- [ ] All 20+ event types registered (`RegisterLearnerEvents`, `RegisterPedagogyEvents`, etc.)
- [ ] `StreamIdentity = StreamIdentity.AsString` configured (student UUID as stream key)
- [ ] `StudentProfileSnapshot` inline snapshot projection configured at interval 100
- [ ] `StudentMasteryProjection` (inline) and `TeacherDashboardProjection` (async) registered

**Test:**
```csharp
[Fact]
public async Task MartenEventStore_CanAppendAndReplay()
{
    await using var session = store.LightweightSession();
    var studentId = Guid.NewGuid().ToString();
    session.Events.Append(studentId, new ConceptAttempted_V1(...));
    await session.SaveChangesAsync();

    var events = await session.Events.FetchStreamAsync(studentId);
    Assert.Single(events);
    Assert.IsType<ConceptAttempted_V1>(events[0].Data);
}
```

---

## DATA-002: Event Type Records
**Priority:** P0
**Blocked by:** DATA-001

**Description:**
Implement all domain event records from `marten-event-store.cs` as C# records with proper serialization.

**Acceptance Criteria:**
- [ ] All 20+ event records compile (`ConceptAttempted_V1` through `OutreachResponseReceived_V1`)
- [ ] Every event has a `Timestamp` field (DateTimeOffset) — NOT `DateTimeOffset.UtcNow`
- [ ] `XpAwarded_V1` has `DifficultyLevel` and `DifficultyMultiplier` fields
- [ ] All records are JSON-serializable via System.Text.Json (CamelCase)
- [ ] No event record contains mutable fields (all are `record` types)

**Test:**
```csharp
[Fact]
public void AllEvents_AreImmutableRecords()
{
    var types = typeof(ConceptAttempted_V1).Assembly.GetTypes()
        .Where(t => t.Name.EndsWith("_V1") && t.IsRecord());
    Assert.True(types.Count() >= 20);
    foreach (var t in types)
        Assert.True(t.GetProperties().All(p => p.SetMethod == null || !p.SetMethod.IsPublic));
}
```

---

## DATA-003: Snapshot Apply Methods (Deterministic)
**Priority:** P0
**Blocked by:** DATA-002

**Description:**
Implement all `Apply()` methods on `StudentProfileSnapshot` using event timestamps, not wall clock.

**Acceptance Criteria:**
- [ ] `Apply(ConceptAttempted_V1)` uses `e.Timestamp` for `LastAttemptedAt`
- [ ] `Apply(ConceptMastered_V1)` uses `e.Timestamp` for `MasteredAt`
- [ ] ZERO occurrences of `DateTimeOffset.UtcNow` in any Apply method
- [ ] Replaying the same events twice produces identical snapshot state (deterministic)
- [ ] `ConceptMasteryState` tracks all fields: PKnown, IsMastered, TotalAttempts, LastAttemptedAt, MasteredAt, LastMethodology

**Test:**
```csharp
[Fact]
public void Snapshot_IsDeterministic_OnReplay()
{
    var events = GenerateTestEvents(50);
    var snap1 = ReplayToSnapshot(events);
    var snap2 = ReplayToSnapshot(events);
    Assert.Equal(JsonSerializer.Serialize(snap1), JsonSerializer.Serialize(snap2));
}
```

---

## DATA-004: CQRS Inline Projections
**Priority:** P1
**Blocked by:** DATA-003

**Description:**
Implement `StudentMasteryProjection` (inline) serving the knowledge graph UI.

**Acceptance Criteria:**
- [ ] `StudentMasteryView` updated on every `ConceptAttempted_V1` and `ConceptMastered_V1`
- [ ] `MasteryMap` dictionary correctly tracks P(known) per concept
- [ ] `ConceptsMastered` count is accurate
- [ ] Projection is inline (`ProjectionLifecycle.Inline`) — zero latency on reads
- [ ] View is queryable: `session.Query<StudentMasteryView>().SingleOrDefault(v => v.Id == studentId)`

**Test:**
```csharp
[Fact]
public async Task MasteryProjection_UpdatesOnAttempt()
{
    // Append ConceptAttempted with posteriorMastery = 0.90
    // Query StudentMasteryView
    // Assert MasteryMap["concept-1"] == 0.90
    // Assert ConceptsMastered == 1
}
```

---

## DATA-005: CQRS Async Projections
**Priority:** P2
**Blocked by:** DATA-004

**Description:**
Implement `TeacherDashboardProjection` and `ParentProgressProjection` (async).

**Acceptance Criteria:**
- [ ] Both projections registered as `ProjectionLifecycle.Async`
- [ ] Marten async daemon starts and processes events in background
- [ ] `ClassOverviewProjection` is `MultiStreamProjection` (NOT `SingleStreamProjection` — cross-student)
- [ ] Projections rebuild correctly from scratch (`marten.RebuildProjection<T>()`)
- [ ] Staleness < 2 seconds under normal load (100 events/second)

**Test:**
```csharp
[Fact]
public async Task AsyncProjection_RebuildsFromScratch()
{
    // Append 1000 events across 10 students
    // Trigger rebuild
    // Verify TeacherDashboardView has correct aggregates
}
```

---

## DATA-006: Neo4j Curriculum Graph
**Priority:** P1
**Blocked by:** None (parallel with DATA-001)

**Description:**
Set up Neo4j (AuraDB or local Docker), load the schema from `neo4j-schema.cypher`.

**Acceptance Criteria:**
- [ ] Neo4j running with constraints and indexes from schema file
- [ ] 5 sample Math concepts loaded with prerequisite edges
- [ ] MCM edges loaded: (ErrorType, ConceptCategory) → [(Methodology, confidence)]
- [ ] MCM hot-path query returns results in < 5ms
- [ ] Cycle detection query returns no cycles in sample data
- [ ] `.NET Neo4j driver` (Neo4j.Driver) configured and connection-tested

**Test:**
```cypher
// Verify MCM lookup works
MATCH (e:ErrorType {name: 'conceptual'})-[r:RECOMMENDS]->(m:Methodology)
WHERE r.concept_category = 'algebra'
RETURN m.name, r.confidence ORDER BY r.confidence DESC
// Expected: [("socratic", 0.85), ("feynman", 0.70), ("analogy", 0.55)]
```

---

## DATA-007: Redis Cache Layer
**Priority:** P1
**Blocked by:** None (parallel)

**Description:**
Set up Redis (ElastiCache or local Docker), implement key schema from `redis-contracts.ts`.

**Acceptance Criteria:**
- [ ] All key namespaces follow `cena:{context}:{entity}:{id}` pattern
- [ ] Session cache: SET with 30-minute TTL
- [ ] Idempotency keys: SET NX with 72-hour TTL
- [ ] Per-student daily token budget: INCR with midnight-UTC TTL reset
- [ ] Rate limiter: sliding window sorted set (100/min API, 20/min LLM, 500/min sync)
- [ ] Knowledge graph cache: 24-hour TTL with NATS invalidation subscriber

**Test:**
```typescript
test('idempotency key prevents duplicates', async () => {
    const key = Keys.idempotency('student-1', 'event-abc');
    const first = await redis.set(key, '1', 'EX', 259200, 'NX'); // 72h
    const second = await redis.set(key, '1', 'EX', 259200, 'NX');
    expect(first).toBe('OK');
    expect(second).toBeNull(); // Already exists
});
```

---

## DATA-008: S3 Analytics Export Schema
**Priority:** P3
**Blocked by:** DATA-003

**Description:**
Implement nightly Parquet export to S3 with anonymization.

**Acceptance Criteria:**
- [ ] Export job runs on schedule (2:00 AM Israel time)
- [ ] Four record types exported: concept_attempts, mastery_changes, methodology_switches, session_summaries
- [ ] All `student_id` fields replaced with HMAC-SHA256 anonymized IDs
- [ ] Per-file SHA-256 checksums in manifest
- [ ] S3 key convention: `cena-exports/v1/{YYYY-MM-DD}/`
- [ ] Export validates against JSON Schema in `s3-export-schema.json`

**Test:**
```python
def test_export_anonymization():
    raw_events = load_test_events()
    exported = anonymize_and_export(raw_events)
    for record in exported:
        assert 'student_id' not in record  # Replaced with anonymous_id
        assert len(record['anonymous_id']) == 64  # SHA-256 hex
```

---

## DATA-009: Upcaster Infrastructure
**Priority:** P2
**Blocked by:** DATA-002

**Description:**
Build and test the event upcasting pipeline — prove that V1→V2 migration works.

**Acceptance Criteria:**
- [ ] At least ONE V2 event defined (e.g., `ConceptAttempted_V2` with one new optional field)
- [ ] Upcaster registered: `opts.Events.Upcast<ConceptAttempted_V1, ConceptAttempted_V2>(...)`
- [ ] Existing V1 events in the store are transparently upcasted on replay
- [ ] Projections handle both V1 and V2 events
- [ ] Integration test: persist V1, register upcaster, replay → get V2

**Test:**
```csharp
[Fact]
public async Task Upcaster_TransformsV1ToV2OnReplay()
{
    // 1. Persist ConceptAttempted_V1
    // 2. Register V1→V2 upcaster
    // 3. Replay stream
    // 4. Assert replayed event is ConceptAttempted_V2
    // 5. Assert new field has default value
}
```

---

## DATA-010: Optimistic Concurrency on Event Append
**Priority:** P0
**Blocked by:** DATA-001

**Description:**
All Marten event appends MUST use expected version for optimistic concurrency.

**Acceptance Criteria:**
- [ ] `session.Events.Append(streamId, expectedVersion, events)` used everywhere
- [ ] Concurrent writes to the same stream throw `EventStreamUnexpectedMaxEventIdException`
- [ ] Actor handles the exception by retrying with fresh state
- [ ] ZERO instances of `session.Events.Append(streamId, event)` without expected version

**Test:**
```csharp
[Fact]
public async Task ConcurrentWrites_ThrowOptimisticConcurrencyException()
{
    var session1 = store.LightweightSession();
    var session2 = store.LightweightSession();
    session1.Events.Append(streamId, 0, event1);
    session2.Events.Append(streamId, 0, event2);
    await session1.SaveChangesAsync(); // succeeds
    await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(
        () => session2.SaveChangesAsync()); // fails — version conflict
}
```

---

## DATA-011: Crypto-Shredding (GDPR Deletion)
**Priority:** P1
**Blocked by:** DATA-001, DATA-007

**Description:**
Implement per-student encryption for GDPR-compliant deletion without breaking the event stream.

**Acceptance Criteria:**
- [ ] Per-student AES-256 encryption key generated on registration, stored in AWS KMS (or Redis for dev)
- [ ] `StudentId` in events is a pseudonymous ID; real→pseudo mapping is encrypted
- [ ] On deletion request: key is destroyed → all events become unreadable for that student
- [ ] Aggregate analytics (MethodologyEffectiveness, RetentionCohort) continue working on anonymized data
- [ ] Marten custom serialization hook encrypts/decrypts PII fields transparently

**Test:**
```csharp
[Fact]
public async Task CryptoShredding_MakesStudentDataUnreadable()
{
    // 1. Create student, persist events
    // 2. Verify events are readable
    // 3. Delete encryption key
    // 4. Attempt to read events → PII fields are garbled/empty
    // 5. Verify anonymized projections still work
}
```

---

## DATA-012: Database Migration & Backup Strategy
**Priority:** P2
**Blocked by:** DATA-001

**Description:**
Document and test the database backup, restore, and migration strategy.

**Acceptance Criteria:**
- [ ] RDS automated snapshots every 1 hour, retained 7 days
- [ ] Tested restore from snapshot → verified event stream integrity
- [ ] Marten `AutoCreateSchemaObjects = CreateOrUpdate` validated on staging
- [ ] Neo4j daily backup to S3 (manual for AuraDB, automated for self-hosted)
- [ ] Redis backup via ElastiCache snapshot (if applicable)
- [ ] Runbook document: "How to restore from backup" with exact commands

**Test:**
```bash
# Restore test (run monthly)
aws rds restore-db-instance-from-db-snapshot \
    --db-instance-identifier cena-restore-test \
    --db-snapshot-identifier cena-auto-snapshot-latest
# Then run: dotnet test --filter "Category=DataIntegrity"
```
