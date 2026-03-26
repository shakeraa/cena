# DATA-007: Redis Key Schema, TTLs, Rate Limiter & Idempotency

**Priority:** P1 — blocks session cache, budget enforcement, offline sync
**Blocked by:** INF-003 (ElastiCache Redis cluster)
**Estimated effort:** 2 days
**Contract:** `contracts/data/redis-contracts.ts`

---

## Context
Redis serves as the fast-path cache layer for the Cena platform. It handles 5 concerns: (1) active session state cache, (2) offline sync idempotency keys, (3) daily token budget counters, (4) sliding-window rate limiting, and (5) knowledge graph query cache. The key schema, TTLs, and value types are defined in `redis-contracts.ts`. All keys use hash tags `{student_id}` for Redis Cluster slot affinity, enabling MULTI/EXEC transactions within a single shard. This task implements the TypeScript key builders, value types, rate limiter, and idempotency checker as production-ready modules.

## Subtasks

### DATA-007.1: Key Builders & TTL Constants
**Files to create/modify:**
- `src/shared/redis/keys.ts` — key builder functions matching `redis-contracts.ts` Keys object
- `src/shared/redis/ttl.ts` — TTL constants and `ttlUntilMidnightUtc()` helper
- `src/shared/redis/client.ts` — Redis Cluster client factory

**Acceptance:**
- [ ] All key builders from `redis-contracts.ts` implemented exactly:
  - `Keys.session(studentId)` -> `cena:session:state:{${studentId}}`
  - `Keys.idempotency(studentId, eventId)` -> `cena:idempotency:event:{${studentId}}:${eventId}`
  - `Keys.tokenBudget(studentId, dateUtc)` -> `cena:budget:tokens:{${studentId}}:${dateUtc}`
  - `Keys.rateLimit(scope, studentId)` -> `cena:ratelimit:${scope}:{${studentId}}`
  - `Keys.kgPrereqs(conceptId)` -> `cena:kg:prereqs:{${conceptId}}`
  - `Keys.kgMcm(errorType, categoryId)` -> `cena:kg:mcm:${errorType}:{${categoryId}}`
  - `Keys.kgNeighbors(conceptId)` -> `cena:kg:neighbors:{${conceptId}}`
- [ ] TTL constants from contract:
  - `SESSION`: 1800 (30 minutes)
  - `IDEMPOTENCY`: 259200 (72 hours)
  - `TOKEN_BUDGET_MAX`: 86400 (24 hours cap)
  - `RATE_LIMIT_WINDOW`: 60 (60 seconds)
  - `KNOWLEDGE_GRAPH`: 86400 (24 hours)
- [ ] `ttlUntilMidnightUtc()` returns seconds until next UTC midnight, clamped to [1, 86400]
- [ ] `todayUtc()` returns YYYY-MM-DD string in UTC
- [ ] Hash tags `{id}` ensure slot affinity in Redis Cluster mode
- [ ] Redis Cluster client connects to ElastiCache endpoint from env `REDIS_CLUSTER_URL`

**Test:**
```typescript
import { Keys, TTL, ttlUntilMidnightUtc, todayUtc } from "./keys";

describe("Key Builders", () => {
  test("session key has correct format with hash tag", () => {
    expect(Keys.session("stu-001")).toBe("cena:session:state:{stu-001}");
  });

  test("idempotency key includes student and event", () => {
    expect(Keys.idempotency("stu-001", "evt-abc")).toBe(
      "cena:idempotency:event:{stu-001}:evt-abc"
    );
  });

  test("token budget key includes date partition", () => {
    expect(Keys.tokenBudget("stu-001", "2026-03-26")).toBe(
      "cena:budget:tokens:{stu-001}:2026-03-26"
    );
  });

  test("rate limit key includes scope", () => {
    expect(Keys.rateLimit("llm", "stu-001")).toBe("cena:ratelimit:llm:{stu-001}");
    expect(Keys.rateLimit("api", "stu-001")).toBe("cena:ratelimit:api:{stu-001}");
    expect(Keys.rateLimit("sync", "stu-001")).toBe("cena:ratelimit:sync:{stu-001}");
  });

  test("kg mcm key includes error type and category", () => {
    expect(Keys.kgMcm("procedural", "cat-arithmetic")).toBe(
      "cena:kg:mcm:procedural:{cat-arithmetic}"
    );
  });
});

describe("TTL Helpers", () => {
  test("TTL constants match contract", () => {
    expect(TTL.SESSION).toBe(1800);
    expect(TTL.IDEMPOTENCY).toBe(259200);
    expect(TTL.TOKEN_BUDGET_MAX).toBe(86400);
    expect(TTL.RATE_LIMIT_WINDOW).toBe(60);
    expect(TTL.KNOWLEDGE_GRAPH).toBe(86400);
  });

  test("ttlUntilMidnightUtc returns positive value", () => {
    const ttl = ttlUntilMidnightUtc();
    expect(ttl).toBeGreaterThan(0);
    expect(ttl).toBeLessThanOrEqual(86400);
  });

  test("todayUtc returns YYYY-MM-DD format", () => {
    const today = todayUtc();
    expect(today).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
```

---

### DATA-007.2: Sliding Window Rate Limiter
**Files to create/modify:**
- `src/shared/redis/rate-limiter.ts` — sliding window rate limiter using sorted sets
- `src/shared/redis/types.ts` — `RateLimitScope`, `RateLimitConfig` types from contract

**Acceptance:**
- [ ] Rate limit scopes and configs from `redis-contracts.ts`:
  - `api`: maxRequests=100, windowSeconds=60
  - `llm`: maxRequests=20, windowSeconds=60
  - `sync`: maxRequests=500, windowSeconds=60
- [ ] Implementation uses Redis sorted set (from contract usage examples):
  1. `ZREMRANGEBYSCORE` to trim entries older than `now - windowSeconds * 1000`
  2. `ZCARD` to count current entries
  3. If count >= maxRequests -> reject (return `{ allowed: false, remaining: 0, resetInMs }`)
  4. `ZADD` with score=timestamp(ms) and member=`${timestamp}-${random}`
  5. `EXPIRE` key with windowSeconds TTL (safety backstop)
- [ ] Key: `cena:ratelimit:${scope}:{${studentId}}` (hash tag for cluster slot affinity)
- [ ] Atomic: all 4 operations in a single pipeline or Lua script to prevent races
- [ ] Returns `RateLimitResult`: `{ allowed: boolean, remaining: number, limit: number, resetInMs: number }`

**Test:**
```typescript
import { RateLimiter } from "./rate-limiter";
import Redis from "ioredis-mock";

describe("RateLimiter", () => {
  let redis: Redis;
  let limiter: RateLimiter;

  beforeEach(() => {
    redis = new Redis();
    limiter = new RateLimiter(redis);
  });

  test("allows requests within limit", async () => {
    const result = await limiter.check("llm", "stu-001");
    expect(result.allowed).toBe(true);
    expect(result.remaining).toBe(19); // 20 limit - 1 used
  });

  test("blocks when limit exceeded", async () => {
    for (let i = 0; i < 20; i++) {
      await limiter.check("llm", "stu-001");
    }
    const result = await limiter.check("llm", "stu-001");
    expect(result.allowed).toBe(false);
    expect(result.remaining).toBe(0);
  });

  test("resets after window expires", async () => {
    for (let i = 0; i < 20; i++) {
      await limiter.check("llm", "stu-001");
    }
    // Advance time past window
    jest.advanceTimersByTime(61_000);
    const result = await limiter.check("llm", "stu-001");
    expect(result.allowed).toBe(true);
  });

  test("api scope has higher limit than llm", async () => {
    for (let i = 0; i < 50; i++) {
      const result = await limiter.check("api", "stu-001");
      expect(result.allowed).toBe(true);
    }
    // Still under 100 limit
    const result = await limiter.check("api", "stu-001");
    expect(result.allowed).toBe(true);
    expect(result.remaining).toBe(49);
  });

  test("sync scope allows 500 per minute", async () => {
    for (let i = 0; i < 500; i++) {
      await limiter.check("sync", "stu-001");
    }
    const result = await limiter.check("sync", "stu-001");
    expect(result.allowed).toBe(false);
  });
});
```

**Edge cases:**
- Clock skew between Redis and app server -> sorted set scores use Redis `TIME` command, not app clock
- Redis connection lost during pipeline -> fail open (allow request), log ERROR
- Very high cardinality (many unique members in sorted set) -> ZREMRANGEBYSCORE keeps set bounded by window

---

### DATA-007.3: Idempotency Checker & Session Cache
**Files to create/modify:**
- `src/shared/redis/idempotency.ts` — offline sync idempotency using SET NX
- `src/shared/redis/session-cache.ts` — session state read/write with `SessionCachePayload` type

**Acceptance:**
- [ ] Idempotency check from contract usage example:
  - `SET NX` with key `cena:idempotency:event:{${studentId}}:${eventId}`, value `"1"`, TTL=72 hours
  - Returns `{ isNew: true }` if key was set (new event, process it)
  - Returns `{ isNew: false }` if key existed (duplicate, skip processing)
  - 72-hour TTL survives typical offline-to-online reconnection windows
- [ ] Session cache implements `SessionCachePayload` interface from contract:
  - `studentId`, `sessionId`, `startedAt`, `currentConceptId`, `activeMethodology`, `fatigueScore`, `questionsAttempted`, `questionsCorrect`, `avgResponseTimeMs`, `deviceType`, `isOffline`, `experimentCohort`, `appVersion`, `pendingOfflineEvents`
- [ ] Session write: `SET` with key `cena:session:state:{${studentId}}`, value=JSON, TTL=30 minutes
- [ ] Session read: `GET` key, parse JSON, return typed `SessionCachePayload | null`
- [ ] Session refresh: on every heartbeat, `EXPIRE` key to reset 30-min TTL
- [ ] Session delete: on `SessionEnded_V1` or explicit logout

**Test:**
```typescript
import { IdempotencyChecker } from "./idempotency";
import { SessionCache, SessionCachePayload } from "./session-cache";
import Redis from "ioredis-mock";

describe("IdempotencyChecker", () => {
  let checker: IdempotencyChecker;

  beforeEach(() => {
    checker = new IdempotencyChecker(new Redis());
  });

  test("first event is new", async () => {
    const result = await checker.check("stu-001", "evt-001");
    expect(result.isNew).toBe(true);
  });

  test("duplicate event is not new", async () => {
    await checker.check("stu-001", "evt-001");
    const result = await checker.check("stu-001", "evt-001");
    expect(result.isNew).toBe(false);
  });

  test("different events are both new", async () => {
    const r1 = await checker.check("stu-001", "evt-001");
    const r2 = await checker.check("stu-001", "evt-002");
    expect(r1.isNew).toBe(true);
    expect(r2.isNew).toBe(true);
  });
});

describe("SessionCache", () => {
  let cache: SessionCache;
  const payload: SessionCachePayload = {
    studentId: "stu-001",
    sessionId: "sess-abc",
    startedAt: "2026-03-26T10:00:00Z",
    currentConceptId: "math-fractions",
    activeMethodology: "socratic",
    fatigueScore: 0.2,
    questionsAttempted: 5,
    questionsCorrect: 3,
    avgResponseTimeMs: 8000,
    deviceType: "tablet",
    isOffline: false,
    experimentCohort: "control",
    appVersion: "2.1.0",
    pendingOfflineEvents: 0,
  };

  beforeEach(() => {
    cache = new SessionCache(new Redis());
  });

  test("write and read session", async () => {
    await cache.set(payload);
    const result = await cache.get("stu-001");
    expect(result).not.toBeNull();
    expect(result!.currentConceptId).toBe("math-fractions");
    expect(result!.fatigueScore).toBe(0.2);
  });

  test("returns null for missing session", async () => {
    const result = await cache.get("nonexistent");
    expect(result).toBeNull();
  });

  test("delete removes session", async () => {
    await cache.set(payload);
    await cache.delete("stu-001");
    const result = await cache.get("stu-001");
    expect(result).toBeNull();
  });
});
```

**Edge cases:**
- Student sends 1000 buffered offline events on reconnect -> idempotency check is O(1) per event (SET NX), batch of 1000 = ~200ms
- Session cache expired (student inactive > 30 min) -> return null, actor rehydrates from Marten event store
- JSON parse error in session cache (corrupted data) -> delete key, return null, log WARNING

---

## Integration Test (all subtasks combined)

```typescript
import Redis from "ioredis-mock";
import { Keys, TTL, todayUtc, ttlUntilMidnightUtc } from "./keys";
import { RateLimiter } from "./rate-limiter";
import { IdempotencyChecker } from "./idempotency";
import { SessionCache, SessionCachePayload } from "./session-cache";

describe("Redis Integration", () => {
  let redis: Redis;

  beforeEach(() => {
    redis = new Redis();
  });

  test("full student session lifecycle", async () => {
    const session = new SessionCache(redis);
    const idempotency = new IdempotencyChecker(redis);
    const limiter = new RateLimiter(redis);

    // 1. Start session
    const payload: SessionCachePayload = {
      studentId: "stu-001", sessionId: "sess-1",
      startedAt: new Date().toISOString(), currentConceptId: "math-fractions",
      activeMethodology: "socratic", fatigueScore: 0.0,
      questionsAttempted: 0, questionsCorrect: 0,
      avgResponseTimeMs: 0, deviceType: "tablet",
      isOffline: false, experimentCohort: null, appVersion: "2.1.0",
      pendingOfflineEvents: 0,
    };
    await session.set(payload);

    // 2. Check rate limit for LLM call
    const rl = await limiter.check("llm", "stu-001");
    expect(rl.allowed).toBe(true);

    // 3. Record token budget (via raw INCR for this test)
    const budgetKey = Keys.tokenBudget("stu-001", todayUtc());
    await redis.incr(budgetKey);
    const used = await redis.get(budgetKey);
    expect(parseInt(used!, 10)).toBe(1);

    // 4. Idempotency check for offline event
    const idempResult = await idempotency.check("stu-001", "offline-evt-1");
    expect(idempResult.isNew).toBe(true);

    // 5. Duplicate rejected
    const dupResult = await idempotency.check("stu-001", "offline-evt-1");
    expect(dupResult.isNew).toBe(false);

    // 6. Session still active
    const active = await session.get("stu-001");
    expect(active!.sessionId).toBe("sess-1");

    // 7. End session
    await session.delete("stu-001");
    expect(await session.get("stu-001")).toBeNull();
  });
});
```

## Rollback Criteria
If Redis causes availability issues:
- Session cache: fall back to in-memory cache per actor instance (no cross-instance sharing)
- Idempotency: fall back to PostgreSQL-based idempotency check (slower, but durable)
- Rate limiter: disable entirely (log only, no enforcement)
- Token budget: move to PostgreSQL counter with advisory locks

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `npm test -- --testPathPattern=redis` -> 0 failures
- [ ] All 7 key builder formats match `redis-contracts.ts` exactly
- [ ] Hash tags verified for Redis Cluster slot affinity
- [ ] TTL constants match contract exactly
- [ ] Rate limiter correctly enforces limits for all 3 scopes (api=100, llm=20, sync=500)
- [ ] Idempotency SET NX correctly deduplicates offline events
- [ ] Session cache round-trips `SessionCachePayload` with all 14 fields
- [ ] PR reviewed by architect (you)
