# Key Takeaways: What to Steal, What to Avoid

---

## Steal These Patterns

### 1. Control Plane / Data Plane Separation
Fortnite cleanly separates orchestration (MCP) from execution (game servers). Cena should formalize:
- **Control plane:** Actor system (orchestration, state, pedagogy decisions)
- **Data plane:** LLM calls (execution of learning interactions)
- **Analytics plane:** Event projections from Marten

### 2. Profile Multiplexing
Multiple profiles per player (`athena`, `campaign`, `common_core`). Cena equivalent: per-subject mastery profiles within the StudentActor, each as a separate event stream or sub-aggregate.

### 3. Feature Flags as a First-Class Service
Fortnite's "Lightswitch" service controls feature availability system-wide. Cena's `MethodologySwitchService` is per-student; add a global feature flag service for:
- Enabling/disabling LLM models system-wide
- A/B testing pedagogy approaches
- Gradual rollout of new features

### 4. Service Decomposition (16+ services)
Fortnite decomposed into 16+ focused services early. Cena's current actor types map to bounded contexts that could become separate deployable services as the system grows.

### 5. Invest in Observability from Day 1
Epic's #1 lesson. Cena already has metrics counters/histograms. Ensure they flow to dashboards and alerts.

---

## Avoid These Anti-Patterns

### 1. Full-Mesh Topology
Fortnite's 101-node XMPP full mesh was O(N^2) and a known scaling bottleneck. Cena uses NATS (no full-mesh problem). Never go full-mesh for messaging.

### 2. Unbounded Write Queues
Fortnite's MCP queued writes indefinitely → 40s DB operations → thread starvation. Always have timeouts and backpressure on writes.

### 3. Aggressive Timeouts Under Load
200ms socket timeout caused connection storms. Under load, slightly slower is better than rapid reconnection churn. Use adaptive timeouts that increase under pressure.

### 4. Read-from-Primary Hidden Paths
Fortnite had API calls silently reading from primary instead of replicas. Audit all read paths to ensure they hit replicas/cache.

### 5. Single Points of Failure in the Messaging Layer
XMPP single LB failure made all users appear offline. NATS avoids this, but audit for SPOFs in other layers (Redis, PostgreSQL primary).

---

## Cena Architectural Advantages over Fortnite

| Advantage                          | Details                                               |
|------------------------------------|-------------------------------------------------------|
| **Event sourcing**                 | Full temporal audit trail; Fortnite uses mutable docs |
| **Proto.Actor virtual actors**     | Simpler than Akka Cluster Sharding, same semantics    |
| **NATS over XMPP**                | Binary, durable, no O(N^2) mesh                      |
| **Circuit breakers built-in**      | Per-model LLM CBs; Fortnite had none in 2018         |
| **Activation backpressure**        | Rate limiter + queue cap; Fortnite had unbounded      |
| **PostgreSQL over MongoDB**        | ACID, native partitioning, Marten ES, no license risk |
| **Redis caching**                  | More capable than Memcached (data structures, pub/sub)|
| **Lean infrastructure**            | Solo architect; Fortnite had teams but still broke    |

---

## Priority Action Items (Ranked)

| # | Action                              | Effort | Impact | Source Lesson                        |
|---|-------------------------------------|--------|--------|--------------------------------------|
| 1 | Add Marten write timeouts           | Low    | High   | Fortnite 40s DB calls killed MCP     |
| 2 | Export metrics to Prometheus/Grafana | Medium | High   | "Invest in logging early"            |
| 3 | Redis circuit breaker               | Low    | Medium | Single cache failure cascaded        |
| 4 | PostgreSQL partitioning by student   | Medium | Medium | Fortnite's 9-shard strategy          |
| 5 | Global health aggregator actor      | Medium | Medium | No coordinated degradation response  |
| 6 | Graceful degradation tiers          | Medium | High   | Fortnite was all-or-nothing          |
| 7 | Profile multiplexing per subject    | Medium | Medium | Per-profile separation reduces bloat |
| 8 | NATS outbox sweep (re-publish)      | Low    | Medium | XMPP fire-and-forget lost messages   |
| 9 | Adaptive timeouts under load        | Low    | Low    | 200ms timeout caused storms          |
|10 | Global feature flag service         | Medium | Low    | Lightswitch service pattern           |

---

## Conference Talks to Watch

1. **KubeCon 2018** -- Paul Sharpe: How Epic uses K8s for Fortnite
2. **HashiConf** -- Paul Sharpe: Cooking with Nomad (why Nomad > K8s for some workloads)
3. **GDC** -- "Connect Players Across Platforms with Epic Online Services"
4. **GDC** -- "Making Friends and Building Networks with Epic Account Services"
5. **GDC** -- "Everything You Need to Build a Modern Cross-Play Enabled Game"

## Architecture Diagram

```
         FORTNITE BACKEND (simplified)
         ═══════════════════════════════

[Game Clients] ──UDP/TCP──▶ [Dedicated UE Servers on EC2]
      │                      (16 per c4.8xlarge)
      │                              │
      ▼                              ▼
[CDN / CloudFront]          [RESTful Backend APIs]
                                     │
              ┌──────────┬───────────┼───────────┬──────────┐
              │          │           │           │          │
         [Account   [MCP Service  [Friends   [Party    [Lightswitch
          Service]   (Java/Akka)]  Service]   Service]  (flags)]
              │          │           │           │
              ▼          ▼           ▼           ▼
         [Memcached] [MongoDB     [XMPP Cluster ─ 101 nodes]
                      9 Shards]   [Full mesh, TCP+WS]
                      (8 user +   [3M+ connections]
                       1 config)  [~600K msg/sec]
                          │
              ┌───────────┼───────────┐
              │                       │
         [Kinesis ─ 5K shards]   [DynamoDB]
         [125M events/min]            │
              │                  [Spark Streaming]
              ▼
         [EMR Hadoop ─ 22 clusters, 4K+ EC2, 8K+ ETL jobs]
```

## Sources

All sources documented in individual research files (00-06).
