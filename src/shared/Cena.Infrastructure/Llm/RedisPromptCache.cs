// =============================================================================
// Cena Platform — RedisPromptCache (prr-047 + prr-233)
//
// Redis-backed IPromptCache implementation. Mirrors the resilience pattern
// already in ExplanationCacheService (Cena.Actors/Services/): Redis failures
// are treated as a cache miss, never bubbled up. Our threat model is an
// explain-path cascade (miss → LLM → exhaust token budget) if we let Redis
// outages propagate.
//
// Metric contract (consumed by deploy/observability/dashboards/
// llm-cache-hit-rate.json + alerting-rules.yaml):
//   cena.prompt_cache.hits_total{cache_type, task, institute_id, exam_target_code}
//   cena.prompt_cache.misses_total{cache_type, task, institute_id, exam_target_code}
//   cena.prompt_cache.errors_total{cache_type, task, op, institute_id, exam_target_code}
//
// prr-233 adds `institute_id` + `exam_target_code` so we can:
//   - Fire a per-(feature, exam_target_code) SLO: 7-day rolling hit rate ≥ 60%.
//   - Derive `cache_savings_usd` per target from (1 - hit_rate) × expected_tokens × tier_rate.
//   - Detect the per-target variation persona-finops predicted (global ~85% →
//     ~68-72% when 4-target plans are common). The global-average view hides
//     the breach; the per-target split surfaces it.
//
// Labels are sourced from the ambient IPromptCacheKeyContext, not the method
// signature, so callers at the [TaskRouting] seam don't need to thread
// `examTargetCode` through every interface. The scheduler's session-start
// path opens a scope via AdaptiveScheduler → context.PushScope(institute,
// examCode) and every downstream LLM call inside that async scope picks up
// the same labels automatically.
//
// When no scope is open (e.g. content-ingestion, migration backfill, a
// host-agnostic system prompt), both labels emit "unknown" — matching
// LlmCostMetric.UnknownInstituteLabel so operators see a consistent
// null-signifier across the cost + cache dashboards.
//
// The dashboard derives hit rate as:
//   sum(rate(hits_total[5m])) / (sum(rate(hits_total[5m])) + sum(rate(misses_total[5m])))
// and alerts when the 7-day rolling rate drops below 60% per (feature,
// exam_target_code) bucket. See deploy/observability/alerting-rules.yaml
// §cena-prompt-cache-per-target.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.Llm;

// prr-046: RedisPromptCache does not itself make LLM calls — it is the
// cache layer that fronts them. The existing [TaskRouting] tag satisfies
// the LLM-routing scanner; [DelegatesLlmCost] satisfies the cost-metric
// ratchet without spuriously emitting cost events (the cost is billed by
// whichever caller is consulting the cache).
// ADR-0047: cache layer; scrubbing is the responsibility of the [TaskRouting]
// service whose prompt is about to be cached. The cache key is a SHA256 hash
// of the prompt (see PromptCacheKeyBuilder), so even if unscrubbed PII reached
// the key-builder, the stored key never contains the raw bytes. The cached
// value is the LLM response, not the input.
// prr-233: The cache-key-derivation path for target-scoped lookups routes
// through IPromptCacheKeyContext, which is itself the ambient read of the
// caller's active-target scope. The cache itself does not construct keys —
// PromptCacheKeyBuilder does — so the bypass marker lives on callers.
[TaskRouting("tier3", "prompt_cache")]
[DelegatesLlmCost("calling service emits cost on cache-miss LLM path")]
[DelegatesTraceIdTo("calling service stamps trace_id on cache-miss LLM path")]
[PiiPreScrubbed("Cache layer — scrubbing is the [TaskRouting] caller's responsibility. Cache key is a SHA256 digest, not the raw prompt; cached value is the LLM response, not the input. See ADR-0047 §Decision 3.")]
[PromptCacheKeyBypassesTargetContext("Cache seam — key construction happens in PromptCacheKeyBuilder call sites. This class reads the ambient IPromptCacheKeyContext for metric labels, which is the intended seam. The arch ratchet applies to constructors, not readers.")]
public sealed class RedisPromptCache : IPromptCache
{
    /// <summary>
    /// Label value emitted when the ambient cache-key context has not opened
    /// a scope. Matches <see cref="LlmCostMetric.UnknownInstituteLabel"/> so
    /// both metrics share a single null-signifier in Prometheus and dashboards
    /// can group by "unknown" without reconciling two spellings.
    /// </summary>
    public const string UnknownLabel = "unknown";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPromptCache> _logger;
    private readonly IPromptCacheKeyContext _keyContext;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _errors;

    public RedisPromptCache(
        IConnectionMultiplexer redis,
        ILogger<RedisPromptCache> logger,
        IMeterFactory meterFactory,
        IPromptCacheKeyContext keyContext)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyContext = keyContext ?? throw new ArgumentNullException(nameof(keyContext));

        var meter = meterFactory.Create("Cena.PromptCache", "1.0.0");
        _hits = meter.CreateCounter<long>(
            "cena.prompt_cache.hits_total",
            description: "Prompt-cache hits labelled by cache_type, task, institute_id, exam_target_code (prr-233)");
        _misses = meter.CreateCounter<long>(
            "cena.prompt_cache.misses_total",
            description: "Prompt-cache misses labelled by cache_type, task, institute_id, exam_target_code (prr-233)");
        _errors = meter.CreateCounter<long>(
            "cena.prompt_cache.errors_total",
            description: "Prompt-cache Redis errors labelled by cache_type, task, op, institute_id, exam_target_code (prr-233)");
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

        var institute = ResolveInstituteLabel();
        var target = ResolveTargetLabel();

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(cacheKey).WaitAsync(ct);

            if (value.IsNullOrEmpty)
            {
                _misses.Add(1,
                    new KeyValuePair<string, object?>("cache_type", cacheType),
                    new KeyValuePair<string, object?>("task", taskName),
                    new KeyValuePair<string, object?>("institute_id", institute),
                    new KeyValuePair<string, object?>("exam_target_code", target));
                return (false, string.Empty);
            }

            _hits.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName),
                new KeyValuePair<string, object?>("institute_id", institute),
                new KeyValuePair<string, object?>("exam_target_code", target));
            return (true, value.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _errors.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName),
                new KeyValuePair<string, object?>("op", "get"),
                new KeyValuePair<string, object?>("institute_id", institute),
                new KeyValuePair<string, object?>("exam_target_code", target));
            _logger.LogWarning(ex,
                "Prompt cache GET failed for key {Key} (cacheType={CacheType}, task={Task}, institute={Institute}, target={Target}); treating as miss.",
                cacheKey, cacheType, taskName, institute, target);
            _misses.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName),
                new KeyValuePair<string, object?>("institute_id", institute),
                new KeyValuePair<string, object?>("exam_target_code", target));
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
            var institute = ResolveInstituteLabel();
            var target = ResolveTargetLabel();
            _errors.Add(1,
                new KeyValuePair<string, object?>("cache_type", cacheType),
                new KeyValuePair<string, object?>("task", taskName),
                new KeyValuePair<string, object?>("op", "set"),
                new KeyValuePair<string, object?>("institute_id", institute),
                new KeyValuePair<string, object?>("exam_target_code", target));
            _logger.LogWarning(ex,
                "Prompt cache SET failed for key {Key} (cacheType={CacheType}, task={Task}, institute={Institute}, target={Target}); response not cached.",
                cacheKey, cacheType, taskName, institute, target);
        }
    }

    private string ResolveInstituteLabel()
    {
        var id = _keyContext.InstituteId;
        return string.IsNullOrWhiteSpace(id) ? UnknownLabel : id!;
    }

    private string ResolveTargetLabel()
    {
        var code = _keyContext.ExamTargetCode;
        return string.IsNullOrWhiteSpace(code) ? UnknownLabel : code!;
    }
}
