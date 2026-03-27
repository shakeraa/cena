# RES-003: Redis Circuit Breaker Actor

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P1 -- Next sprint                            |
| **Effort**    | Low (2-3 hours)                              |
| **Impact**    | Medium -- prevents cache failure cascade     |
| **Origin**    | Fortnite Feb 2018: Memcached saturation → Nginx thread exhaustion → LB ejection |
| **Status**    | TODO                                         |

---

## Problem

`StudentActor` depends on Redis for caching (`IConnectionMultiplexer` injected). If Redis becomes unavailable or slow, actor activation blocks on cache read. No circuit breaker protects this path.

Fortnite's Memcached failure cascaded identically: cache timeout → thread exhaustion → health check failure → total outage.

## Design

Reuse the existing `LlmCircuitBreakerActor` pattern. Same state machine (Closed → Open → HalfOpen), different config.

### New Config

```csharp
// In CircuitBreakerConfig.cs
public static CircuitBreakerConfig Redis => new("redis", 5, TimeSpan.FromSeconds(30));
```

### Integration with StudentActor

When Redis CB is **Open**:
- **Skip cache read** on activation → load directly from Marten (slower but correct)
- **Skip cache write** on state change → accept eventual consistency
- Log warning: "Redis circuit open, falling back to Marten-only"

When Redis CB **Closes**:
- Resume normal cache read/write
- Warm the cache on next state change

### Actor Hierarchy

```
RootSupervisor
  ├── LlmCircuitBreakerActor("kimi")    ← existing
  ├── LlmCircuitBreakerActor("sonnet")  ← existing
  ├── LlmCircuitBreakerActor("opus")    ← existing
  ├── CircuitBreakerActor("redis")       ← NEW
  └── StudentActorManager
        └── StudentActor(s)
```

## Affected Files

- `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` -- generalize or create sibling
- `src/actors/Cena.Actors/Students/StudentActor.cs` -- add CB check before Redis calls
- `src/actors/Cena.Actors.Host/Program.cs` -- register Redis CB actor

## Acceptance Criteria

- [ ] `CircuitBreakerConfig.Redis` defined (maxFailures=5, openDuration=30s)
- [ ] Redis circuit breaker actor spawned at startup
- [ ] StudentActor checks Redis CB before cache operations
- [ ] Fallback to Marten-only when Redis CB is open
- [ ] Metric: `cena.redis.circuit_opened_total`
- [ ] Unit test: simulate Redis failure, verify fallback to Marten
- [ ] Unit test: verify CB closes after 3 successful probes
