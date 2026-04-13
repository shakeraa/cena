// =============================================================================
// Cena Platform — Distributed Token Bucket Rate Limiting (RATE-001)
// Redis-backed token buckets for per-student and per-classroom rate limits.
// =============================================================================

using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.RateLimit;

/// <summary>
/// Distributed token bucket rate limiting service.
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Attempts to acquire <paramref name="tokens"/> from the bucket.
    /// Returns true if allowed, false if rate limited.
    /// </summary>
    Task<RateLimitResult> TryAcquireAsync(
        string partitionKey,
        string scope,
        int capacity,
        int refillRatePerSecond,
        int tokens = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Gets current bucket state for observability.
    /// </summary>
    Task<BucketState> GetBucketStateAsync(
        string partitionKey,
        string scope,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public sealed record RateLimitResult(bool Allowed, int RemainingTokens, DateTimeOffset? RetryAfter);

/// <summary>
/// Current bucket state for dashboard/metrics.
/// </summary>
public sealed record BucketState(int RemainingTokens, int Capacity, DateTimeOffset LastRefill);

/// <summary>
/// Redis-backed token bucket implementation using atomic Lua scripts.
/// </summary>
public sealed class RedisRateLimitService : IRateLimitService
{
    private readonly IDatabase _redis;
    private readonly ILogger<RedisRateLimitService> _logger;

    private const string LuaScript = @"
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local refillRate = tonumber(ARGV[2])
        local requested = tonumber(ARGV[3])
        local nowMs = tonumber(ARGV[4])

        local data = redis.call('HMGET', key, 'tokens', 'last_refill_ms')
        local tokens = tonumber(data[1])
        local lastRefillMs = tonumber(data[2])

        if tokens == nil then
            tokens = capacity
            lastRefillMs = nowMs
        end

        local elapsedSec = math.max(0, (nowMs - lastRefillMs) / 1000.0)
        local newTokens = math.min(capacity, tokens + math.floor(elapsedSec * refillRate))

        if newTokens >= requested then
            newTokens = newTokens - requested
            redis.call('HMSET', key, 'tokens', newTokens, 'last_refill_ms', nowMs)
            redis.call('EXPIRE', key, 86400)
            return {1, newTokens}
        else
            redis.call('HMSET', key, 'tokens', newTokens, 'last_refill_ms', nowMs)
            redis.call('EXPIRE', key, 86400)
            local deficit = requested - newTokens
            local waitMs = math.ceil(deficit / refillRate * 1000)
            return {0, waitMs}
        end
    ";

    public RedisRateLimitService(IConnectionMultiplexer redis, ILogger<RedisRateLimitService> logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<RateLimitResult> TryAcquireAsync(
        string partitionKey,
        string scope,
        int capacity,
        int refillRatePerSecond,
        int tokens = 1,
        CancellationToken ct = default)
    {
        var key = $"cena:ratelimit:{scope}:{partitionKey}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            var result = (RedisValue[])await _redis.ScriptEvaluateAsync(LuaScript,
                new RedisKey[] { key },
                new RedisValue[] { capacity, refillRatePerSecond, tokens, nowMs });

            var allowed = (int)result[0] == 1;
            if (allowed)
            {
                return new RateLimitResult(true, (int)result[1], null);
            }

            var waitMs = (int)result[1];
            var retryAfter = DateTimeOffset.UtcNow.AddMilliseconds(waitMs);
            return new RateLimitResult(false, 0, retryAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis rate limit check failed for {Key} — failing open", key);
            return new RateLimitResult(true, capacity, null);
        }
    }

    public async Task<BucketState> GetBucketStateAsync(
        string partitionKey,
        string scope,
        CancellationToken ct = default)
    {
        var key = $"cena:ratelimit:{scope}:{partitionKey}";
        var data = await _redis.HashGetAsync(key, new RedisValue[] { "tokens", "last_refill_ms" });
        var tokens = data[0].TryParse(out int t) ? t : 0;
        var lastRefill = data[1].TryParse(out long lr) ? DateTimeOffset.FromUnixTimeMilliseconds(lr) : DateTimeOffset.UtcNow;
        return new BucketState(tokens, 0, lastRefill);
    }
}
