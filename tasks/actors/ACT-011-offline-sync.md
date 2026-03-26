# ACT-011: Idempotent Offline Sync Handler with Redis Dedup

**Priority:** P1 — blocks offline learning
**Blocked by:** ACT-001 (Cluster), INF-004 (Redis), SEC-006 (HMAC)
**Estimated effort:** 2 days
**Contract:** `contracts/frontend/offline-sync-client.ts` (SyncRequest/SyncResult), `contracts/data/redis-contracts.ts` (idempotency keys)

---

## Context

When a student reconnects after offline learning, the client sends a `SyncRequest` with queued events. The server must process each event idempotently (using UUIDv7 idempotency keys in Redis with 72-hour TTL), validate HMAC signatures, reconcile with server state, and return authoritative mastery recalculation.

## Subtasks

### ACT-011.1: Sync Endpoint + Idempotency Check

**Files to create/modify:**
- `src/Cena.Web/Controllers/SyncController.cs`
- `src/Cena.Web/Services/IdempotencyService.cs`

**Acceptance:**
- [ ] `POST /api/sync` accepts `SyncRequest`, returns `SyncResult`
- [ ] Each event checked against Redis: `SET NX cena:idempotency:event:{studentId}:{eventId} "1" EX 259200`
- [ ] Duplicate events (NX fails) -> skipped, included in `SyncResult.duplicateCount`
- [ ] Queue checksum validated before processing
- [ ] Rate limited: 500 events per sync batch (per redis-contracts.ts)

**Test:**
```csharp
[Fact]
public async Task Sync_DeduplicatesReplayedEvents()
{
    var request = CreateSyncRequest(events: new[] { event1, event1 }); // Duplicate
    var result = await _syncController.Sync(request);
    Assert.Equal(1, result.ProcessedCount);
    Assert.Equal(1, result.DuplicateCount);
}
```

---

### ACT-011.2: Event Processing + Mastery Recalculation

**Files to create/modify:**
- `src/Cena.Web/Services/SyncProcessor.cs`

**Acceptance:**
- [ ] Events classified per `EVENT_CLASSIFICATION_MAP`: unconditional (always apply), conditional (validate against server state), server-authoritative (recalculate)
- [ ] `ExerciseAttempted` events: validate HMAC, apply to BKT batch update
- [ ] `ConceptMastered` events: ignore (server-authoritative, recalculated)
- [ ] Post-processing: server recalculates mastery using `IBktService.BatchUpdate()`
- [ ] `SyncResult.recalculatedState` contains authoritative mastery map

**Test:**
```csharp
[Fact]
public async Task Sync_RecalculatesMastery()
{
    var events = new[] {
        CreateAttempt("math-1", correct: true),
        CreateAttempt("math-1", correct: true),
        CreateAttempt("math-1", correct: false),
    };
    var result = await _syncProcessor.Process("student-1", events);
    Assert.True(result.RecalculatedState["math-1"] > 0.5);
}
```

---

### ACT-011.3: Conflict Resolution + Response

**Files to create/modify:**
- `src/Cena.Web/Services/ConflictResolver.cs`

**Acceptance:**
- [ ] Server state divergence detected: if offline events conflict with server events that occurred during offline period
- [ ] Resolution strategy: server wins for mastery calculations, client events preserved as historical record
- [ ] `SyncResult` includes: `processedCount`, `duplicateCount`, `rejectedCount`, `conflicts[]`, `recalculatedState`
- [ ] Clock skew adjustment applied per `IClockSkewDetector` contract

**Test:**
```csharp
[Fact]
public async Task Sync_HandlesServerDivergence()
{
    // Server already processed a ConceptMastered event while student was offline
    await _eventStore.Append("student-1", new ConceptMastered_V1 { ConceptId = "math-1" });
    var offlineEvents = new[] { CreateAttempt("math-1", correct: true) };
    var result = await _syncProcessor.Process("student-1", offlineEvents);
    Assert.True(result.RecalculatedState.ContainsKey("math-1"));
}
```

---

## Rollback Criteria
- Disable offline sync; students must be online to learn (severe UX degradation)

## Definition of Done
- [ ] Sync endpoint processes offline batches idempotently
- [ ] Redis dedup prevents double-processing
- [ ] Mastery recalculated server-authoritatively
- [ ] PR reviewed by architect
