# INF-019: Redis Circuit Breaker on Explanation Cache Path

**Priority:** P1 — Redis outage can cascade into 10x LLM token costs
**Blocked by:** Nothing
**Estimated effort:** 1 day

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The personalized explanation pipeline has a 3-tier fallback:
- **L1**: Static explanation from question metadata (free)
- **L2**: Cached personalized explanation from Redis (free, fast)
- **L3**: Live LLM call via Claude API (costs tokens, 25k/day budget per student)

If Redis goes down, every explanation request falls through L2 → L3, burning the daily token budget in minutes. The `LlmCircuitBreakerActor` gates LLM calls for rate limiting, but nothing gates the _cache miss path_ itself. A Redis outage should trigger fallback to L1 (static), not L3 (LLM).

Additionally, `ConversationThreadActor` stores messages in Redis Streams. A Redis outage makes messaging unavailable — but the actor currently doesn't distinguish "cache miss" from "Redis down" and will retry indefinitely.

## Subtasks

### INF-019.1: Redis Health Check + Circuit Breaker

**Files:**
- `src/actors/Cena.Actors/Infrastructure/RedisCircuitBreaker.cs` — new: lightweight CB around IConnectionMultiplexer
- `src/actors/Cena.Actors.Host/Program.cs` — register as singleton

**Acceptance:**
- [ ] `IRedisCircuitBreaker` interface: `IsAvailable`, `RecordSuccess()`, `RecordFailure()`
- [ ] State machine: `Closed → Open (after 3 failures in 30s) → HalfOpen (after 60s) → Closed (on success)`
- [ ] When `Open`: all Redis calls skip immediately (no timeout waiting)
- [ ] Metric: `cena.redis.circuit_breaker_state` gauge (0=closed, 1=open, 2=half-open)
- [ ] Metric: `cena.redis.circuit_breaker_trips_total` counter
- [ ] Log: `Redis circuit breaker opened after {N} failures` at Warning level

### INF-019.2: Explanation Cache Fallback

**Files:**
- `src/actors/Cena.Actors/Services/ExplanationCacheService.cs` — inject `IRedisCircuitBreaker`
- `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs` — respect CB state

**Acceptance:**
- [ ] `GetAsync()`: if circuit breaker is Open → return `null` immediately (no Redis call)
- [ ] `SetAsync()`: if circuit breaker is Open → skip silently (don't block on dead Redis)
- [ ] L2 cache miss + CB Open → fall through to L1 static, NOT L3 LLM
- [ ] Log: `Explanation cache bypassed — Redis circuit breaker open` at Debug level
- [ ] Token budget is protected: Redis down → static explanations only, no LLM calls

### INF-019.3: ConversationThreadActor Redis Resilience

**Files:**
- `src/actors/Cena.Actors/Messaging/ConversationThreadActor.cs` — inject `IRedisCircuitBreaker`

**Acceptance:**
- [ ] On Redis failure: respond with `MessagingResult(Success: false, ErrorCode: "REDIS_UNAVAILABLE")`
- [ ] Don't retry indefinitely — fail fast, let the client retry
- [ ] When circuit breaker transitions to Closed: log `Redis recovered — messaging available`

## Definition of Done
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Simulated Redis outage: no LLM token budget burn
- [ ] Simulated Redis outage: messaging returns proper error, no hung actors
- [ ] Circuit breaker transitions verified in tests
