# Circuit Breakers & Backpressure: Fortnite vs Cena

---

## Fortnite's Resilience Gaps (2018)

In February 2018, Fortnite had **none of these patterns** properly implemented:

### No Circuit Breakers
Memcached failures cascaded into Nginx thread exhaustion → load balancer health check failures → full service outage. A circuit breaker on the Memcached path would have prevented propagation.

### No Backpressure
MCP writes queued indefinitely. When DB update times spiked to 40,000+ ms, threads blocked forever. No mechanism to shed load.

### No Bulkhead Isolation
Failure in the matchmaking shard affected all services sharing the database layer. No service segmentation.

### Connection Storms (Anti-Pattern)
200ms socket timeout caused rapid disconnect/reconnect churn. Under load, this amplified the failure instead of dampening it.

---

## Cena's Resilience (Current)

### LlmCircuitBreakerActor -- Per-Model Circuit Breaker

Cena has a proper state machine circuit breaker as an actor:

```
Closed (normal) → Open (after maxFailures) → HalfOpen (probe)
```

**Per-model configuration:**
| Model  | MaxFailures | OpenDuration | HalfOpen Probes |
|--------|-------------|--------------|-----------------|
| Kimi   | 5           | 60s          | 3               |
| Sonnet | 3           | 90s          | 3               |
| Opus   | 2           | 120s         | 3               |

**HalfOpen behavior:** Only 1 probe request at a time. 3 consecutive successes → Closed. Any failure → back to Open. This is more sophisticated than what Fortnite had in 2018.

**Metrics built-in:**
- `cena.llm.circuit_opened_total` (counter)
- `cena.llm.requests_rejected_total` (counter)
- Status query returns full state including `EstimatedCloseAt`

### StudentActorManager -- Activation Backpressure

```
Incoming Activations → Rate Limiter (200/sec) → Queue (depth 1000) → Actor Pool (max 10K)
```

- Leaky bucket rate limiter: 200 activations/second
- Queue depth 1000: rejects beyond that
- Hard cap: 10,000 concurrent actors
- Graceful drain: `DrainAll` with timeout for shutdown
- Stop/resume activations: for maintenance windows

### OneForOne Supervision

Child failure (LearningSessionActor, StagnationDetector, OutreachScheduler) restarts only that child. Parent StudentActor is unaffected. Max 3 retries in 60 seconds.

---

## Gap Analysis

### What Cena Has That Fortnite Lacked

| Pattern                     | Cena                                    | Fortnite (2018) |
|-----------------------------|-----------------------------------------|-----------------|
| Circuit breaker (LLM)      | Per-model actor with full state machine | None            |
| Activation rate limiting    | 200/sec leaky bucket                    | None            |
| Actor pool cap              | 10K hard limit                          | None (unbounded)|
| Supervision isolation       | OneForOne (child-only restart)          | Unknown         |
| Queue backpressure          | Depth 1000, reject beyond               | Unbounded queue |

### What Cena Still Needs

| Gap                              | Risk Level | Description                                     |
|----------------------------------|------------|-------------------------------------------------|
| **DB write timeout**             | HIGH       | Marten writes have no explicit timeout. Fortnite's 40s DB calls would block the actor's mailbox. |
| **Redis circuit breaker**        | MEDIUM     | No CB on Redis path. If Redis dies, StudentActor activation (cache load) blocks. |
| **NATS publish timeout**         | MEDIUM     | NatsOutboxPublisher needs fail-fast on NATS unavailability. |
| **Cascade detection**            | LOW        | No global health signal. If 3+ circuit breakers open simultaneously, no coordinated response. |
| **Load shedding by priority**    | LOW        | All activations treated equally. No priority for active sessions vs new sign-ups. |

---

## Recommended Additions

### 1. Marten Write Timeout (Immediate)

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
try
{
    await session.SaveChangesAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Event persist timed out -- let supervision handle it
    throw new EventPersistTimeoutException(_studentId);
}
```

### 2. Redis Circuit Breaker (Next Sprint)

Reuse the same `LlmCircuitBreakerActor` pattern but for Redis:

```csharp
public static CircuitBreakerConfig Redis => new("redis", 5, TimeSpan.FromSeconds(30));
```

When Redis CB opens, StudentActor should:
- Skip cache read on activation (load directly from Marten -- slower but works)
- Skip cache write on state change (eventual consistency when CB closes)

### 3. Global Health Aggregator (Future)

A root-level actor that monitors all circuit breaker states:

```
HealthAggregatorActor
  ├── watches: LlmCB(kimi), LlmCB(sonnet), LlmCB(opus), RedisCB, NatsCB
  ├── if 2+ open → publish DegradedMode event
  └── if 3+ open → trigger LoadSheddingMode
```

## Source

- [Fortnite Postmortem - 3.4M CCU](https://www.fortnite.com/news/postmortem-of-service-outage-at-3-4m-ccu?lang=en-US)
- [Fortnite Postmortem - April 2018](https://www.fortnite.com/news/postmortem-of-service-outage-4-12)
