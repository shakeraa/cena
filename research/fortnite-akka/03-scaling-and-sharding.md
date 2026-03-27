# Scaling & Sharding: Fortnite's Strategy vs Cena

---

## Fortnite's MongoDB Sharding

**9 MongoDB shards:**
- 8 shards for user-specific data (profiles, inventory, stats)
- 1 shard for matchmaking sessions, shared caches, runtime config
- Each shard: 1 writer, 2 read replicas, 1 hidden replica

**Connection topology:**
```
Service Instance → Sidecar Process → mongos Router → MongoDB Shards
                   (connection pool)   (throttled)    (9 shards)
```

**Bottleneck discovered:** Matchmaking on a single shard consumed 15% of all queries and 11% of all writes. Hot shard.

## Fortnite's Kubernetes Scaling

- Amazon EKS for backend services
- Custom StatefulSet controller for game state across pod restarts
- Custom CNI plugin (15% UDP latency reduction)
- GeoDNS-based intelligent routing
- Hub-and-spoke network topology (regional hubs → core network)

## Fortnite Chose Nomad Over K8s for UEFN

Paul Sharpe: *"[Kubernetes] would have been a monumental amount of extra work, particularly due to the multistage workflow, filesystem and Windows requirements."*

The UEFN "cooking pipeline" (20,000 concurrent operations, 2,000+ hosts) runs on HashiCorp Nomad because:
- Multi-stage workflows with filesystem dependencies
- Windows node requirements
- Simpler operational model for batch-style workloads

---

## Cena's Scaling Model

### Current: Proto.Actor Virtual Actors + Cluster

```
Proto.Actor Cluster
  └── StudentActorManager (singleton, pool manager)
        ├── Max 10,000 concurrent actors
        ├── 200 activations/sec rate limiter
        ├── Queue depth 1,000 (backpressure)
        └── Graceful drain for shutdown
```

**State persistence:** PostgreSQL (Marten) -- single database, no sharding yet.

### Key Differences

| Aspect                | Fortnite                               | Cena                               |
|-----------------------|----------------------------------------|------------------------------------|
| DB sharding           | 9 MongoDB shards from early on         | Single PostgreSQL                  |
| Shard key             | Player ID (user data shards)           | N/A                                |
| Hot shard problem     | Yes (matchmaking shard)                | N/A yet                            |
| Actor distribution    | Akka Cluster Sharding (automatic)      | Proto.Actor Cluster (virtual actors)|
| Connection pooling    | Sidecar + mongos + throttling          | Direct connection pool             |
| Read replicas         | 2 per shard + 1 hidden                 | PostgreSQL streaming replicas TBD  |

---

## Insights for Cena

### 1. PostgreSQL Partitioning Strategy (Plan Now)

Don't wait for scale to shard. PostgreSQL native partitioning on `student_id` gives you Fortnite's sharding benefit without operational complexity:

```sql
-- Marten event store partitioned by student_id hash
CREATE TABLE mt_events (
    ...
) PARTITION BY HASH (stream_id);

-- 8 partitions (mirrors Fortnite's 8 user shards)
CREATE TABLE mt_events_p0 PARTITION OF mt_events FOR VALUES WITH (modulus 8, remainder 0);
CREATE TABLE mt_events_p1 PARTITION OF mt_events FOR VALUES WITH (modulus 8, remainder 1);
-- ... etc
```

This is transparent to Marten queries and gives parallel I/O across partitions.

### 2. Separate Hot Data (Fortnite's Lesson)

Fortnite's matchmaking-on-one-shard was a hot spot. Cena's equivalent hot data:
- **Active session state** (high write frequency during learning sessions)
- **BKT mastery updates** (computed and written on every answer)

Consider: Redis for active session state, PostgreSQL for durable event store. The StudentActor already has Redis injected -- use it for session-scoped state that doesn't need event sourcing.

### 3. Connection Sidecar Pattern (When Scaling)

Fortnite's sidecar → mongos → shards pattern is worth adopting when Cena goes multi-node:

```
Actor Process → PgBouncer (sidecar) → PostgreSQL Primary
                                     → PostgreSQL Replicas (reads)
```

PgBouncer as a sidecar provides connection pooling, throttling, and read/write splitting -- same role as Fortnite's mongos with `ShardingTaskExecutorPoolMaxConnecting`.

### 4. Actor Memory Budget (Already Good)

Cena's 500KB per actor / 10K max / memory alerting at 80% is solid. Fortnite didn't publish per-actor memory budgets but their outages suggest they learned this lesson the hard way.

### 5. Rate Limiting Activations (Already Good)

Cena's 200 activations/sec leaky bucket in StudentActorManager is exactly the pattern Fortnite needed during their connection storm. This is a strong defense.

## Source

- [Containers on a Fortnite Scale (KubeCon)](https://superuser.openinfra.org/articles/containers-on-a-fornite-scale/)
- [Epic Relies on Nomad (The Stack)](https://www.thestack.technology/epic-relies-on-nomad-to-keep-gamers-fantasy-islands-afloat/)
- [HashiConf: Cooking with Nomad](https://www.hashicorp.com/en/resources/cooking-with-nomad-powering-the-fortnite-creator-ecosystem)
