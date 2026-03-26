# ACT-012: Outbox Publisher for Catch-Up NATS Publishes

**Priority:** P2 — blocks event reliability
**Blocked by:** ACT-001 (Cluster), INF-002 (RDS)
**Estimated effort:** 1 day
**Contract:** `contracts/backend/nats-subjects.md`

---

## Context

If NATS is temporarily unavailable when an actor persists an event to Marten, the event is stored but never published. The outbox pattern ensures eventual delivery: events written to an outbox table, a background publisher polls for unpublished events and publishes them to NATS.

## Subtasks

### ACT-012.1: Outbox Table + Writer

**Files to create/modify:**
- `src/Cena.Data/Outbox/OutboxTable.cs`
- `src/Cena.Data/EventStore/OutboxEventStore.cs` — Marten middleware that writes to outbox on event append

**Acceptance:**
- [ ] Outbox table: `{ id, stream_id, event_type, event_data, nats_subject, created_at, published_at, retry_count }`
- [ ] Event and outbox entry written in same PostgreSQL transaction (atomic)
- [ ] `published_at` is NULL until successfully published to NATS

**Test:**
```csharp
[Fact]
public async Task OutboxWriter_CreatesEntryOnEventAppend()
{
    await _eventStore.Append("student-1", new ConceptAttempted_V1 { });
    var outboxEntries = await GetUnpublishedOutboxEntries();
    Assert.Single(outboxEntries);
}
```

---

### ACT-012.2: Background Publisher

**Files to create/modify:**
- `src/Cena.Actors.Host/BackgroundServices/OutboxPublisher.cs`

**Acceptance:**
- [ ] Polls outbox table every 5 seconds for unpublished entries
- [ ] Publishes to NATS with `Nats-Msg-Id` for deduplication
- [ ] On success: sets `published_at`
- [ ] On failure: increments `retry_count`, exponential backoff up to 5 minutes
- [ ] Max retries: 10, then move to dead letter table

**Test:**
```csharp
[Fact]
public async Task OutboxPublisher_PublishesAndMarksComplete()
{
    await InsertOutboxEntry(eventType: "ConceptAttempted", subject: "cena.learner.events.ConceptAttempted");
    await _publisher.ProcessBatch();
    var entry = await GetOutboxEntry();
    Assert.NotNull(entry.PublishedAt);
}
```

---

### ACT-012.3: Catch-Up on Startup

**Files to create/modify:**
- `src/Cena.Actors.Host/BackgroundServices/OutboxPublisher.cs` (startup catch-up)

**Acceptance:**
- [ ] On service startup: publish all unpublished outbox entries before accepting new requests
- [ ] Ordered by `created_at` to maintain event ordering
- [ ] Catch-up completes within 60 seconds for up to 10,000 entries

**Test:**
```csharp
[Fact]
public async Task OutboxPublisher_CatchesUpOnStartup()
{
    await InsertOutboxEntries(count: 100);
    await _publisher.CatchUp();
    var unpublished = await GetUnpublishedOutboxEntries();
    Assert.Empty(unpublished);
}
```

---

## Rollback Criteria
- Disable outbox; accept potential event loss during NATS outages

## Definition of Done
- [ ] Outbox ensures eventual NATS delivery
- [ ] Catch-up on startup verified
- [ ] PR reviewed by architect
