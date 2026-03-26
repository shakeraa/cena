# SEC-007: Rate Limiting — API 100/min, WS 20/min, LLM 20/min, Sync 500/req (Redis Sliding Window)

**Priority:** P0 — blocks production deployment
**Blocked by:** INF-004 (Redis)
**Estimated effort:** 2 days
**Contract:** `contracts/data/redis-contracts.ts` (RATE_LIMITS, Keys.rateLimit), `contracts/REVIEW_security.md` (H-4: WebSocket DDoS)

---

## Context

The platform has three existing rate limit scopes (api, llm, sync) but NO WebSocket rate limiting. An attacker can flood SignalR with `SubmitAnswer` commands at thousands/second, overwhelming the Proto.Actor cluster, PostgreSQL, and NATS. This task adds a WebSocket scope and implements all four scopes using Redis sorted-set sliding windows.

## Subtasks

### SEC-007.1: Redis Sliding Window Implementation

**Files to create/modify:**
- `src/Cena.Web/RateLimit/SlidingWindowRateLimiter.cs` — Redis-backed sliding window
- `src/Cena.Web/RateLimit/RateLimitConfiguration.cs` — scope definitions
- `src/Cena.Web/RateLimit/RateLimitMiddleware.cs` — ASP.NET Core middleware

**Acceptance:**
- [ ] Four scopes: `api` (100/min), `websocket` (20/min), `llm` (20/min), `sync` (500/req per batch)
- [ ] Redis sorted set: score = timestamp (epoch ms), member = unique request ID
- [ ] On each request: `ZREMRANGEBYSCORE` to trim expired, `ZCARD` to count, `ZADD` if under limit
- [ ] All operations atomic via Lua script (no race conditions)
- [ ] Key pattern: `cena:ratelimit:{scope}:{studentId}` (slot affinity via hash tag)
- [ ] Key TTL: 2x window duration (auto-cleanup)
- [ ] Rate limit headers: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`
- [ ] 429 Too Many Requests response with `Retry-After` header (seconds until next window)
- [ ] WebSocket scope: applied per-message in SignalR hub, not per-connection

**Test:**
```csharp
[Fact]
public async Task RateLimiter_BlocksAfterLimit()
{
    var limiter = new SlidingWindowRateLimiter(_redis, scope: "api", maxRequests: 5, windowSeconds: 60);
    for (int i = 0; i < 5; i++)
    {
        var result = await limiter.TryAcquire("student-1");
        Assert.True(result.Allowed);
    }
    var blocked = await limiter.TryAcquire("student-1");
    Assert.False(blocked.Allowed);
    Assert.True(blocked.RetryAfterSeconds > 0);
}

[Fact]
public async Task RateLimiter_SlidingWindowExpiresOldEntries()
{
    var limiter = new SlidingWindowRateLimiter(_redis, scope: "api", maxRequests: 2, windowSeconds: 1);
    await limiter.TryAcquire("student-1");
    await limiter.TryAcquire("student-1");
    var blocked = await limiter.TryAcquire("student-1");
    Assert.False(blocked.Allowed);

    await Task.Delay(1100); // Window expires
    var allowed = await limiter.TryAcquire("student-1");
    Assert.True(allowed.Allowed);
}
```

**Edge cases:**
- Redis unavailable -> fail OPEN (allow request), log ERROR, increment circuit breaker counter
- Clock skew between Redis nodes -> use Redis server time (`TIME` command) not client time
- Hash tag collision -> extremely unlikely with UUIDv7, accept risk

---

### SEC-007.2: SignalR WebSocket Rate Limiting

**Files to create/modify:**
- `src/Cena.Web/Hubs/SessionHub.cs` — add per-message rate check
- `src/Cena.Web/RateLimit/WebSocketRateLimitFilter.cs` — SignalR filter

**Acceptance:**
- [ ] Every SignalR command (`SubmitAnswer`, `RequestHint`, `SkipQuestion`, `SwitchApproach`) rate-checked
- [ ] Limit: 20 commands/minute per student (sufficient for normal sessions: ~1 answer/30s)
- [ ] Exceeding limit: message silently dropped, `429` error sent back via SignalR error channel
- [ ] Connection-level protection: max 5 connections per student (prevents connection multiplication)
- [ ] Burst protection: max 5 commands in any 5-second window (catch rapid-fire attacks)
- [ ] Metrics: `cena.ratelimit.websocket.blocked_total` counter

**Test:**
```csharp
[Fact]
public async Task WebSocket_BlocksRapidFireCommands()
{
    var connection = await CreateAuthenticatedSignalRConnection("student-1");
    for (int i = 0; i < 25; i++)
    {
        await connection.InvokeAsync("SubmitAnswer", new { exerciseId = $"q-{i}", answer = "42" });
    }
    // After 20, should receive rate limit error
    var errors = await GetSignalRErrors(connection);
    Assert.True(errors.Count >= 5);
    Assert.All(errors, e => Assert.Contains("rate_limit", e.Code));
}
```

**Edge cases:**
- Student submits answer at exactly the rate limit boundary -> allow (inclusive)
- SignalR reconnection after disconnect -> rate limit resets (new connection, same student ID)
- Admin/teacher connections exempt from WebSocket rate limiting

---

### SEC-007.3: Rate Limit Monitoring + Alerting

**Files to create/modify:**
- `src/Cena.Web/RateLimit/RateLimitMetrics.cs` — OpenTelemetry metrics
- `config/grafana/dashboards/rate-limiting.json` — Grafana dashboard
- `config/grafana/alerts/rate-limit-alerts.yaml` — alert rules

**Acceptance:**
- [ ] Metrics exported: `cena.ratelimit.requests_total` (labels: scope, student, allowed/blocked)
- [ ] Metrics exported: `cena.ratelimit.blocked_total` (labels: scope)
- [ ] Grafana dashboard: requests/min per scope, block rate, top-10 blocked students
- [ ] Alert: if block rate > 10% for any scope -> WARNING to Slack
- [ ] Alert: if single student blocked > 50 times in 5 minutes -> CRITICAL (potential attack)
- [ ] Alert fires -> student's SignalR connection forcefully closed

**Test:**
```csharp
[Fact]
public void RateLimitMetrics_IncrementOnBlock()
{
    var metrics = new RateLimitMetrics();
    metrics.RecordRequest("api", "student-1", allowed: false);
    Assert.Equal(1, metrics.GetBlockedCount("api"));
}
```

---

## Rollback Criteria
If rate limiting causes legitimate session interruptions:
- Double all limits (200/min API, 40/min WS, 40/min LLM, 1000/req sync)
- Disable WebSocket rate limiting first (newest, least tested)
- Keep API and LLM rate limiting (most critical for cost control)

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] `dotnet test --filter "Category=RateLimit"` -> 0 failures
- [ ] Load test: 1000 req/min from single student -> correctly throttled after limit
- [ ] Grafana dashboard shows real-time rate limit metrics
- [ ] PR reviewed by architect
