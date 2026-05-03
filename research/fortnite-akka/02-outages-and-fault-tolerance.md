# Outages & Fault Tolerance: Lessons from Fortnite for Cena

---

## The Two Major Outages

### Outage 1: February 3-4, 2018 (3.4M CCU)

**What happened:** Growth from 1M to 3.4M concurrent users faster than infrastructure could handle.

**Cascade sequence:**
1. DB update times spiked to 40,000+ ms per operation
2. MCP thread pool misconfiguration reduced available workers → threads blocked waiting on DB
3. Memcached saturated → Nginx auth proxy timed out (100ms timeout) → consumed all Nginx workers
4. Nginx failed health checks → load balancer ejected Nginx nodes
5. XMPP single load balancer failure → all users appeared offline
6. **No bulkhead isolation** → failure propagated across every service

**Root causes:**
- Thread pool tuning was wrong (configuration change created invisible bottleneck)
- No circuit breakers on the Memcached path
- No backpressure on MCP DB writes (queued indefinitely)
- Single point of failure on XMPP load balancer

### Outage 2: April 11-12, 2018 (v3.5 Release)

**What happened:** Account Service MongoDB degraded for ~18 hours.

**Cascade sequence:**
1. Dataset exceeded available RAM → expensive data movement in/out of cache
2. Socket timeout was only 200ms → connection churn storm (rapid disconnect/reconnect)
3. Connection counts spiked as slow multi-hour crescendo
4. Certain API calls read from primary nodes instead of replicas → amplified hotspot

**Root causes:**
- MongoDB working set outgrew available memory
- Aggressive timeout (200ms) caused more harm than good under load
- Read-from-primary pattern was a hidden bottleneck discovered only at peak

---

## Epic's Fixes

| Fix                                      | Category           |
|------------------------------------------|--------------------|
| Bumped `socketTimeoutMS` from 200ms → 30s | Timeout tuning     |
| Added `maxTimeMS=500ms` to queries        | Fail-fast          |
| Deployed mongos with connection throttling | Backpressure       |
| XMPP load balancer redundancy             | HA                 |
| Improved monitoring and alerting           | Observability      |
| "Invest in logging early"                  | Culture            |

---

## What Cena Already Has (vs Fortnite's Gaps)

| Fortnite Gap (2018)                      | Cena Status                                  |
|------------------------------------------|----------------------------------------------|
| No circuit breakers                      | **LlmCircuitBreakerActor** -- per-model CB with Closed→Open→HalfOpen, configurable thresholds per LLM |
| No backpressure                          | **StudentActorManager** -- 200 activations/sec rate limiter, queue depth 1000, max 10K actors |
| No bulkhead isolation                    | **OneForOne supervision** -- child failure doesn't affect siblings |
| Single LB point of failure               | NATS (distributed by design, no single LB)    |
| Thread pool starvation                   | Proto.Actor's mailbox model (no shared thread pool blocking) |
| No fail-fast on DB calls                 | Marten event store (PostgreSQL) -- needs explicit query timeouts |

### Cena is ahead of 2018 Fortnite in resilience. But there are gaps:

---

## Action Items for Cena

### 1. Add Query Timeouts to Marten (HIGH)

Fortnite learned the hard way: unbounded DB calls kill everything. Cena's StudentActor persists events to Marten but has no explicit timeout.

```csharp
// Current (inferred): await session.SaveChangesAsync();
// Needed: await session.SaveChangesAsync(cancellationToken with timeout);
```

Add a `CancellationTokenSource` with 500ms-2s timeout on all Marten writes. If Marten is slow, fail fast and let the supervision strategy handle it.

### 2. Memory Pressure Circuit Breaker (MEDIUM)

Fortnite's second outage was caused by dataset exceeding RAM. Cena's StudentActorManager caps at 10K actors (~500KB each = ~5GB peak). Add:
- Memory pressure monitoring (already has `MemoryCheckInterval`)
- Proactive passivation when memory exceeds 80% threshold
- Reject new activations under memory pressure (like circuit breaker but for memory)

### 3. Connection Pool Monitoring (MEDIUM)

Fortnite's connection storms were invisible until catastrophic. Monitor:
- PostgreSQL connection pool utilization
- Redis connection count
- NATS connection state
- Alert when any pool exceeds 70% utilization

### 4. Graceful Degradation Tiers (LOW -- plan now, build later)

Fortnite had no degradation path -- it was either 100% or down. Cena should define tiers:

| Tier | Trigger                    | Response                                        |
|------|----------------------------|-------------------------------------------------|
| 0    | Normal                     | Full features                                   |
| 1    | LLM circuit open           | Fallback to simpler pedagogy (pre-built items)  |
| 2    | DB latency >500ms          | Read-only mode (serve from Redis cache)         |
| 3    | Memory >80%                | Aggressive passivation, reject new sessions     |
| 4    | Multiple systems degraded  | Static content mode, queue all writes            |

### 5. Observability from Day 1 (CRITICAL -- Epic's top lesson)

Epic explicitly said: *"Invest in comprehensive logging early. Metrics collection is a conversation that happens with a system over time."*

Cena already has `System.Diagnostics.Metrics` counters and histograms. Ensure:
- All metrics are exported to a time-series DB (Prometheus/InfluxDB)
- Dashboard exists showing: active actors, event persist latency, circuit breaker states, BKT computation time
- Alerts on: event persist latency >100ms, circuit breaker opens, activation rate >150/sec

## Sources

- [Postmortem of Service Outage at 3.4M CCU](https://www.fortnite.com/news/postmortem-of-service-outage-at-3-4m-ccu?lang=en-US)
- [Postmortem of Service Outage 4/11-4/12/2018](https://www.fortnite.com/news/postmortem-of-service-outage-4-12)
- [HN Discussion on 3.4M CCU Outage](https://news.ycombinator.com/item?id=16340462)
- [Building for 100M Players (Distributed Systems Lessons)](https://michaeleakins.com/insights/building-for-100m-players-fortnite-distributed-systems/)
