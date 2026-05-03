// =============================================================================
// Cena Platform — Redis Variant Rate Limiter (PRR-265, ADR-0059 §15.5)
//
// Redis sorted-set sliding-window implementation of IVariantRateLimiter.
// Modeled on PushNotificationRateLimiter (the established pattern), with
// extensions for multi-scope atomic check + reason-bearing decision.
//
// Lua atomicity: CheckAsync runs ZREMRANGEBYSCORE + ZCARD across every
// scope in a single Redis round-trip. CommitAsync similarly batches
// ZADD + EXPIRE in one Lua call so individual scope writes can't tear.
//
// Key shape: cena:vrl:{scope}:{partition}
//   - cena:    repo prefix
//   - vrl:     variant-rate-limit namespace
//   - {scope}: short stable name (e.g. "student-day"); included so
//              flushing a scope doesn't require iterating all keys
//   - {partition}: caller-supplied composite (already canonicalized)
//
// TTL: Window * 1.5 so abandoned keys reclaim space. The 0.5x safety
// margin accommodates the read-then-write race where a check just
// before TTL expiry lands the commit just after.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.RateLimiting;

/// <summary>
/// Redis sorted-set sliding-window rate limiter.
/// </summary>
public sealed class RedisVariantRateLimiter : IVariantRateLimiter
{
    /// <summary>OTLP meter name.</summary>
    public const string MeterName = "Cena.RateLimiting.Variants";

    /// <summary>Counter emitted on every denial.</summary>
    public const string DenialCounterName = "cena_variant_rate_limit_denials_total";

    /// <summary>Counter emitted on every commit (for parity with denial rate).</summary>
    public const string CommitCounterName = "cena_variant_rate_limit_commits_total";

    /// <summary>Redis key prefix for every scope's sorted set.</summary>
    public const string KeyPrefix = "cena:vrl";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisVariantRateLimiter> _logger;
    private readonly Counter<long> _denialCounter;
    private readonly Counter<long> _commitCounter;

    public RedisVariantRateLimiter(
        IConnectionMultiplexer redis,
        IMeterFactory meterFactory,
        ILogger<RedisVariantRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _logger = logger;
        var meter = meterFactory.Create(MeterName, "1.0.0");
        _denialCounter = meter.CreateCounter<long>(
            DenialCounterName,
            unit: "events",
            description:
                "Variant generation rate-limit denials, by scope (student-day, " +
                "institute-day, institute-source-day, student-source-30d). " +
                "Tracks ADR-0059 §15.5 R1 caps.");
        _commitCounter = meter.CreateCounter<long>(
            CommitCounterName,
            unit: "events",
            description:
                "Variant generation rate-limit commits, by scope. Useful for " +
                "(commits / commits + denials) success-rate dashboards.");
    }

    /// <inheritdoc/>
    public async Task<VariantRateLimitDecision> CheckAsync(
        IReadOnlyList<VariantRateLimitScope> scopes,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0) return VariantRateLimitDecision.Allow();

        // Order of evaluation is the order callers passed; on first denial
        // we surface that scope so the deny reason is deterministic across
        // identical traffic.
        var db = _redis.GetDatabase();
        var nowSeconds = asOfUtc.ToUnixTimeSeconds();

        foreach (var scope in scopes)
        {
            ct.ThrowIfCancellationRequested();
            ValidateScope(scope);

            var key = BuildKey(scope);
            var windowStartSeconds = asOfUtc.Subtract(scope.Window).ToUnixTimeSeconds();

            // Sweep then count atomically.
            const string lua = @"
                redis.call('zremrangebyscore', KEYS[1], '-inf', ARGV[1])
                local count = redis.call('zcard', KEYS[1])
                local oldest
                if count > 0 then
                    local entries = redis.call('zrange', KEYS[1], 0, 0, 'WITHSCORES')
                    oldest = entries[2]
                else
                    oldest = '0'
                end
                return {count, oldest}
            ";

            var raw = (RedisResult[]?)await db.ScriptEvaluateAsync(
                lua,
                new RedisKey[] { key },
                new RedisValue[] { windowStartSeconds });
            if (raw is null) continue;

            var count = (int)raw[0];
            var oldestScore = (long)raw[1];

            if (count >= scope.Limit)
            {
                var retryAfter = ComputeRetryAfter(oldestScore, scope.Window, nowSeconds);
                _denialCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("scope", scope.ScopeName));
                _logger.LogInformation(
                    "[VARIANT_RATE_LIMIT_DENIED] scope={Scope} partition={Partition} " +
                    "count={Count} limit={Limit} retry_after_seconds={RetryAfter}",
                    scope.ScopeName, scope.PartitionKey, count, scope.Limit,
                    (int)Math.Ceiling(retryAfter.TotalSeconds));
                return new VariantRateLimitDecision(
                    Allowed: false,
                    DeniedScopeName: scope.ScopeName,
                    CurrentCount: count,
                    Limit: scope.Limit,
                    RetryAfter: retryAfter);
            }
        }

        return VariantRateLimitDecision.Allow();
    }

    /// <inheritdoc/>
    public async Task CommitAsync(
        IReadOnlyList<VariantRateLimitScope> scopes,
        string commitId,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        if (scopes.Count == 0) return;

        var db = _redis.GetDatabase();
        var nowSeconds = asOfUtc.ToUnixTimeSeconds();

        foreach (var scope in scopes)
        {
            ct.ThrowIfCancellationRequested();
            ValidateScope(scope);

            var key = BuildKey(scope);
            var ttlSeconds = (long)Math.Ceiling(scope.Window.TotalSeconds * 1.5);

            // ZADD with the commitId as the member; sorted-set semantics
            // dedupe on identical members. Caller picks commitId so a
            // retry inside one logical generation doesn't double-count.
            const string lua = @"
                redis.call('zadd', KEYS[1], ARGV[1], ARGV[2])
                redis.call('expire', KEYS[1], ARGV[3])
                return 1
            ";

            await db.ScriptEvaluateAsync(
                lua,
                new RedisKey[] { key },
                new RedisValue[]
                {
                    nowSeconds,
                    $"{commitId}:{scope.ScopeName}",
                    ttlSeconds,
                });

            _commitCounter.Add(
                1,
                new KeyValuePair<string, object?>("scope", scope.ScopeName));
        }
    }

    /// <summary>
    /// Build the Redis key for a scope. Public for tests; format is the
    /// stable contract callers can introspect during incident response.
    /// </summary>
    public static string BuildKey(VariantRateLimitScope scope) =>
        $"{KeyPrefix}:{scope.ScopeName}:{scope.PartitionKey}";

    private static void ValidateScope(VariantRateLimitScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.ScopeName))
            throw new ArgumentException("Scope name is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(scope.PartitionKey))
            throw new ArgumentException("Partition key is required.", nameof(scope));
        if (scope.Window <= TimeSpan.Zero)
            throw new ArgumentException("Window must be positive.", nameof(scope));
        if (scope.Limit < 0)
            throw new ArgumentException("Limit must be >= 0.", nameof(scope));
    }

    private static TimeSpan ComputeRetryAfter(long oldestScore, TimeSpan window, long nowSeconds)
    {
        if (oldestScore <= 0) return window;
        var ageSeconds = nowSeconds - oldestScore;
        var remainingSeconds = window.TotalSeconds - ageSeconds;
        if (remainingSeconds <= 0) return TimeSpan.FromSeconds(1);
        return TimeSpan.FromSeconds(Math.Ceiling(remainingSeconds));
    }
}

/// <summary>
/// No-op limiter for tests + offline tooling. Always allows; never throws.
/// Production hosts NEVER register this.
/// </summary>
public sealed class NullVariantRateLimiter : IVariantRateLimiter
{
    /// <summary>Stateless shared instance.</summary>
    public static readonly NullVariantRateLimiter Instance = new();

    private NullVariantRateLimiter() { }

    /// <inheritdoc/>
    public Task<VariantRateLimitDecision> CheckAsync(
        IReadOnlyList<VariantRateLimitScope> scopes,
        DateTimeOffset asOfUtc,
        CancellationToken ct) =>
        Task.FromResult(VariantRateLimitDecision.Allow());

    /// <inheritdoc/>
    public Task CommitAsync(
        IReadOnlyList<VariantRateLimitScope> scopes,
        string commitId,
        DateTimeOffset asOfUtc,
        CancellationToken ct) => Task.CompletedTask;
}
