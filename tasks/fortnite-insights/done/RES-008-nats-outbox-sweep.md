# RES-008: NATS Outbox Sweep (Re-Publish)

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P1 -- Reliability                            |
| **Effort**    | Low (2-3 hours)                              |
| **Impact**    | Medium -- prevents message loss on NATS downtime |
| **Origin**    | Fortnite's XMPP was fire-and-forget with no delivery guarantee. Messages lost silently. |
| **Status**    | DONE                                         |
| **Execution** | See [EXECUTION.md](EXECUTION.md#res-008-nats-outbox-sweep--p1-mostly-solved) |

---

## Problem

`NatsOutboxPublisher` persists events to Marten then publishes to NATS. If NATS is temporarily unavailable, the publish fails but the event is already persisted. Without a sweep mechanism, these events are never delivered to NATS consumers (teacher dashboards, analytics, outreach triggers).

## Design

### Outbox Table Schema

Events in Marten should have an `is_published` flag (or a separate outbox table):

```sql
CREATE TABLE cena_outbox (
    id              BIGSERIAL PRIMARY KEY,
    event_id        UUID NOT NULL,
    stream_id       TEXT NOT NULL,
    subject         TEXT NOT NULL,         -- NATS subject
    payload         JSONB NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at    TIMESTAMPTZ,           -- NULL = not yet published
    retry_count     INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_outbox_unpublished ON cena_outbox (created_at)
    WHERE published_at IS NULL;
```

### Sweep Process

A background actor or hosted service that runs every 10 seconds:

```
OutboxSweepActor (singleton, 10s interval)
  1. SELECT * FROM cena_outbox WHERE published_at IS NULL AND created_at < now() - interval '5s' LIMIT 100
  2. For each: publish to NATS subject
  3. On NATS ack: UPDATE cena_outbox SET published_at = now()
  4. On NATS failure: INCREMENT retry_count, skip (retry next sweep)
  5. If retry_count > 10: log error, move to dead-letter table
```

### Metric

```csharp
private static readonly Counter<long> OutboxRepublished =
    Meter.CreateCounter<long>("cena.outbox.republished_total");
private static readonly Counter<long> OutboxDeadLettered =
    Meter.CreateCounter<long>("cena.outbox.dead_lettered_total");
```

## Affected Files

- `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs` -- write to outbox table
- New: `src/actors/Cena.Actors/Infrastructure/OutboxSweepActor.cs`
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` -- outbox table setup
- `src/actors/Cena.Actors.Host/Program.cs` -- register sweep actor

## Acceptance Criteria

- [ ] Events written to outbox table before NATS publish
- [ ] Successful NATS publish marks event as published
- [ ] Sweep re-publishes unpublished events older than 5 seconds
- [ ] Dead-letter after 10 retries with alert
- [ ] Metric: `cena.outbox.republished_total` and `cena.outbox.dead_lettered_total`
- [ ] Integration test: block NATS, verify events accumulate, unblock, verify delivery
