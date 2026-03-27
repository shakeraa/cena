# RES-004: PostgreSQL Partitioning by Student ID

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P2 -- Plan now, implement before scale       |
| **Effort**    | Medium (6-8 hours)                           |
| **Impact**    | Medium -- parallel I/O, prevents hot table   |
| **Origin**    | Fortnite's 9-shard MongoDB strategy (8 user shards + 1 config shard) |
| **Status**    | TODO                                         |
| **Execution** | See [EXECUTION.md](EXECUTION.md#res-004-postgresql-partitioning--p2) |

---

## Problem

Cena's Marten event store is a single PostgreSQL table (`mt_events`). As the student count grows, this becomes a write hotspot -- the same problem Fortnite hit with their MongoDB shards.

## Design

Use PostgreSQL native hash partitioning on `stream_id` (which maps to student ID for student event streams). Transparent to Marten queries.

### Migration SQL

```sql
-- Step 1: Create partitioned table (during maintenance window)
CREATE TABLE mt_events_partitioned (
    LIKE mt_events INCLUDING ALL
) PARTITION BY HASH (stream_id);

-- Step 2: Create 8 partitions (mirrors Fortnite's 8 user shards)
CREATE TABLE mt_events_p0 PARTITION OF mt_events_partitioned
    FOR VALUES WITH (modulus 8, remainder 0);
CREATE TABLE mt_events_p1 PARTITION OF mt_events_partitioned
    FOR VALUES WITH (modulus 8, remainder 1);
-- ... p2 through p7

-- Step 3: Migrate data
INSERT INTO mt_events_partitioned SELECT * FROM mt_events;

-- Step 4: Swap tables
ALTER TABLE mt_events RENAME TO mt_events_old;
ALTER TABLE mt_events_partitioned RENAME TO mt_events;
```

### Marten Configuration

Marten should work transparently with partitioned tables. Verify:
- Event appends route to correct partition automatically
- Stream reads query only the relevant partition
- Snapshots can be partitioned separately

## Pre-Requisites

- Benchmark current single-table performance as baseline
- Test Marten compatibility with partitioned tables in a dev environment
- Document rollback plan (rename tables back)

## Acceptance Criteria

- [ ] Benchmark: single-table write throughput (events/sec)
- [ ] Dev environment: partitioned table with 8 partitions
- [ ] Marten integration test: event append + read works across partitions
- [ ] Benchmark: partitioned write throughput (events/sec) -- expect ~2-4x improvement
- [ ] Migration script tested with rollback
- [ ] Snapshot table partitioned separately (if applicable)

## Why Not Now

This is a scale preparation task. At Cena's current size, a single table is fine. But Fortnite learned that sharding under pressure is much harder than sharding proactively. Plan the migration now, execute when approaching 10K+ active students.
