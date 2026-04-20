// =============================================================================
// Cena Platform — RedisPromptCache (prr-047)
//
// Redis-backed IPromptCache implementation. Mirrors the resilience pattern
// already in ExplanationCacheService (Cena.Actors/Services/): Redis failures
// are treated as a cache miss, never bubbled up. Our threat model is an
// explain-path cascade (miss → LLM → exhaust token budget) if we let Redis
// outages propagate.
//
// Metric contract (consumed by deploy/observability/dashboards/
// llm-cache-hit-rate.json + alerting-rules.yaml):
//   cena.prompt_cache.hits_total{cache_type, task}
//   cena.prompt_cache.misses_total{cache_type, task}
//   cena.prompt_cache.errors_total{cache_type, task, op}
//
// The dashboard derives hit rate as:
//   sum(rate(hits_total[5m])) / (sum(rate(hits_total[5m])) + sum(rate(misses_total[5m])))
// and alerts when the 1-hour average drops below 20%, target 40%.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.Llm;

[TaskRouting("tier3", "prompt_cache")]
public sealed class RedisPromptCache : IPromptCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPromptCache> _logger;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _errors;

    public RedisPromptCache(
        IConnectionMultiplexer redis,
        ILogger<RedisPromptCache> logger,
        IMeterFactory meterFactory)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var meter = meterFactory.Create("Cena.PromptCache", "1.0.0");
        _hits = meter.CreateCounter<long>(
            "cena.prompt_cache.hits_total",
            description: "Prompt-cache hits labelled by cache_type (sys|explain|ctx) and task name");
        _misses = meter.CreateCounter<long>(
            "cena.prompt_cache.misses_total",
            description: "Prompt-cache misses labelled by cache_type and task name");
        _errors = meter.CreateCounter<long>(
            "cena.prompt_cache.errors_total",
            description: "Prompt-cache Redis errors labelled by cache_type, task and op (get|set)");
    }

    public async Task<(bool found, string response)> TryGetAsync(
        string cacheKey,
        string cacheType,
        string taskName,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheType);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(cacheKey).WaitAsync(ct);

            if (value.IsNullOrEmpty)
            {
                _misses.Add(1,
                    new KeyValuePair<string, object?>("cache_type", cacheType),
                    new KeyValuePair<string, object?>("task", taskName));
                return (false, string.Empty);
            }

            _hits.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName));
            return (true, value.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _errors.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName),
                new KeyValuePair<string, object?>("op", "get"));
            _logger.LogWarning(ex,
                "Prompt cache GET failed for key {Key} (cacheType={CacheType}, task={Task}); treating as miss.",
                cacheKey, cacheType, taskName);
            _misses.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName));
            return (false, string.Empty);
        }
    }

    public async Task SetAsync(
        string cacheKey,
        string response,
        TimeSpan ttl,
        string cacheType,
        string taskName,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheType);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(response);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "TTL must be positive.");
        }

        try
        {
            var db = _redis.GetDatabase();
            // 3-arg overload matches ExplanationCacheService's house style
            // (StringSetAsync(key, value, ttl)); the driver turns the TimeSpan
            // into an Expiration internally. Callers just see the TTL they
            // passed in.
            await db.StringSetAsync(cacheKey, response, ttl).WaitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _errors.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName),
                new KeyValuePair<string, object?>("op", "set"));
            _logger.LogWarning(ex,
                "Prompt cache SET failed for key {Key} (cacheType={CacheType}, task={Task}); response not cached.",
                cacheKey, cacheType, taskName);
        }
    }
}
