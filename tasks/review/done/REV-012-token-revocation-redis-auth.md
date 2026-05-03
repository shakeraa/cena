# REV-012: Fix Token Revocation Fail-Open & Add Redis Authentication

**Priority:** P1 -- HIGH (revoked/banned users retain access during Redis outage)
**Blocked by:** None
**Blocks:** None
**Estimated effort:** 4 hours
**Source:** System Review 2026-03-28 -- Cyber Officer 1 (Finding 3), Cyber Officer 2 (F-NET-04), Backend Senior (M1)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Two related Redis security issues:

1. **Token revocation fails open**: `TokenRevocationMiddleware` catches `RedisConnectionException` and allows the request through. A suspended admin retains full access during Redis outage. The code comment says "fail-open for availability" -- but for an education platform under FERPA, security must win over availability for auth decisions.

2. **Redis has no authentication**: The Docker Compose runs Redis without `--requirepass`. Any process on the Docker network can read/write the revocation keys (`revoked:{uid}`), the token cache, and messaging data.

## Architect's Decision

**Fail-closed with degradation**: When Redis is down, deny all requests that require revocation checks (authenticated API calls), but keep health and readiness endpoints available. This is the correct trade-off for student data protection.

Add a **local in-memory cache** of recently-checked UIDs (LRU, 5-minute TTL) as a performance optimization that also provides partial resilience during brief Redis blips -- if the UID was checked within the last 5 minutes and was NOT revoked, allow it through. If Redis is down AND the UID is not in the local cache, deny.

## Subtasks

### REV-012.1: Change Token Revocation to Fail-Closed with Local Cache

**File to modify:** `src/shared/Cena.Infrastructure/Auth/TokenRevocationMiddleware.cs`

```csharp
// Add in-memory cache of recently-verified (non-revoked) UIDs
private static readonly MemoryCache _recentlyVerified = new(new MemoryCacheOptions
{
    SizeLimit = 10_000 // max 10K cached UIDs
});

private static readonly MemoryCacheEntryOptions _cacheOptions = new()
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    Size = 1
};

// In InvokeAsync:
try
{
    var isRevoked = await _redis.GetDatabase()
        .KeyExistsAsync($"revoked:{uid}");

    if (isRevoked)
    {
        _recentlyVerified.Remove(uid);
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Token has been revoked" });
        return;
    }

    // UID verified as not-revoked -- cache it
    _recentlyVerified.Set(uid, true, _cacheOptions);
}
catch (RedisConnectionException ex)
{
    _logger.LogError(ex, "Redis unavailable for revocation check on UID {Uid}", uid);

    // Check local cache -- if recently verified as not-revoked, allow
    if (_recentlyVerified.TryGetValue(uid, out _))
    {
        _logger.LogWarning("Redis down, allowing UID {Uid} from local cache (verified < 5min ago)", uid);
        // Fall through to next middleware
    }
    else
    {
        // Redis down AND UID not in cache -- fail closed
        _logger.LogError("Redis down and UID {Uid} not in local cache. Denying request.", uid);
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Authentication service temporarily unavailable. Please try again."
        });
        return;
    }
}
```

**Acceptance:**
- [ ] When Redis is up: behavior unchanged (revoked users get 401)
- [ ] When Redis is down + UID was verified < 5min ago: request allowed (cached)
- [ ] When Redis is down + UID NOT in cache: request denied with 503
- [ ] Revocation of a UID immediately evicts it from local cache
- [ ] Health/readiness endpoints bypass revocation check entirely
- [ ] Log level is ERROR for Redis failures (not WARNING)

### REV-012.2: Add Redis Authentication

**File to modify:** `docker-compose.yml`

```yaml
redis:
  image: redis:7-alpine
  command: >
    redis-server
    --maxmemory 256mb
    --maxmemory-policy allkeys-lru
    --requirepass ${REDIS_PASSWORD:-cena_dev_redis}
  ports:
    - "6380:6379"
```

**Files to modify (connection strings):**
- `src/actors/Cena.Actors.Host/Program.cs` -- add password to Redis config
- `src/api/Cena.Api.Host/Program.cs` -- add password to Redis config

```csharp
var options = ConfigurationOptions.Parse(redisConnectionString);
options.Password = builder.Configuration["Redis:Password"]
    ?? Environment.GetEnvironmentVariable("REDIS_PASSWORD")
    ?? (builder.Environment.IsDevelopment() ? "cena_dev_redis" : null);
options.AbortOnConnectFail = false;
options.ConnectRetry = 3;
```

**Acceptance:**
- [ ] Redis rejects unauthenticated connections
- [ ] Both hosts connect with password from config/env var
- [ ] Dev default password used only in Development environment
- [ ] Non-development throws if password not configured

### REV-012.3: Restrict CORS Methods and Headers

**File to modify:** `src/api/Cena.Api.Host/Program.cs`

```csharp
// BEFORE
policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();

// AFTER
policy.WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
    .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With")
    .AllowCredentials();
```

**Acceptance:**
- [ ] Only specified HTTP methods are allowed via CORS
- [ ] Only specified headers are allowed
- [ ] Pre-flight OPTIONS requests return correct Access-Control-Allow-Methods
