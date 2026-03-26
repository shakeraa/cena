# INF-004: ElastiCache Redis 7.x — Single-Node, Secrets Manager, Key Validation

**Priority:** P0 — blocks rate limiting, session cache, token budget
**Blocked by:** INF-001 (VPC)
**Estimated effort:** 1 day
**Contract:** `contracts/data/redis-contracts.ts` (key schema, TTLs, rate limits)

---

## Context

Redis serves as the caching and rate-limiting layer. ElastiCache Redis 7.x in cluster mode with hash-tag slot affinity for per-student transactions. Connection via Secrets Manager AUTH token.

## Subtasks

### INF-004.1: Terraform ElastiCache Module

**Files to create/modify:**
- `infra/terraform/modules/redis/main.tf`
- `infra/terraform/modules/redis/variables.tf`

**Acceptance:**
- [ ] Redis 7.x, `cache.r6g.large` (prod), `cache.t4g.micro` (dev)
- [ ] Cluster mode enabled with 3 shards (prod), single node (dev)
- [ ] AUTH token stored in Secrets Manager, injected via ECS env
- [ ] Encryption in transit (TLS) and at rest
- [ ] Memory policy: `allkeys-lfu`
- [ ] Security group: port 6379 from ECS tasks only
- [ ] Automatic failover enabled (prod)
- [ ] Backup: daily snapshot with 7-day retention

**Test:**
```bash
redis-cli -h $REDIS_HOST -p 6379 --tls -a $REDIS_AUTH ping
# Assert: PONG
```

---

### INF-004.2: Key Schema Validation + Smoke Test

**Files to create/modify:**
- `tests/Infra/RedisKeyValidationTests.cs`
- `scripts/infra/redis_smoke_test.sh`

**Acceptance:**
- [ ] All key builders from `redis-contracts.ts` produce valid keys with hash tags
- [ ] Smoke test: write session cache, read back, verify TTL
- [ ] Smoke test: sliding window rate limit works correctly
- [ ] Smoke test: idempotency key SET NX works
- [ ] No CROSSSLOT errors for per-student operations

**Test:**
```csharp
[Fact]
public async Task SessionCache_RoundTrip()
{
    var key = Keys.Session("student-1");
    await _redis.StringSetAsync(key, JsonSerializer.Serialize(testPayload), TimeSpan.FromMinutes(30));
    var result = await _redis.StringGetAsync(key);
    Assert.True(result.HasValue);
}

[Fact]
public void KeyBuilder_ProducesHashTag()
{
    var key = Keys.RateLimit("api", "student-1");
    Assert.Contains("{student-1}", key);
}
```

---

## Rollback Criteria
- Fall back to in-memory caching (ConcurrentDictionary) for dev/staging
- Rate limiting degrades to IP-based instead of student-based

## Definition of Done
- [ ] ElastiCache running in staging
- [ ] All key patterns validated, no CROSSSLOT errors
- [ ] Smoke tests pass
- [ ] PR reviewed by architect
