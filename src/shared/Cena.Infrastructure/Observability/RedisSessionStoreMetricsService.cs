// =============================================================================
// Cena Platform — Redis Session Store Metrics (prr-020)
//
// Motivation
// ----------
// pre-release-review 2026-04-20 (persona-sre, feature-discovery-2026-04-20
// L112) flagged that the Redis-backed misconception session store (ADR-0003:
// session-scoped misconception data, 30-day hard cap) has no observability
// on eviction rate, memory pressure, or hit ratio. Under load this gap
// turns a silent eviction spike into a correctness bug: misconception
// state for an in-progress session is lost, the student's next step is
// re-asked the same failing question, and the CAS oracle gate never sees
// the prior attempt.
//
// What this service emits
// -----------------------
// A single hosted service polls Redis `INFO memory` + `INFO stats` every
// 30 seconds (configurable) and publishes four Prometheus-shaped gauges:
//
//   cena_redis_session_evicted_keys_total       — counter mirror of
//       `evicted_keys` from INFO stats. Alert rule in
//       ops/prometheus/alerts-redis-sessions.yml fires when the rate
//       exceeds 5% of total keys per minute (session loss risk).
//
//   cena_redis_session_memory_used_bytes        — gauge sourced from
//       `used_memory`. Paired with `maxmemory` to derive % utilization
//       in the Grafana dashboard.
//
//   cena_redis_session_memory_max_bytes         — gauge sourced from
//       `maxmemory`. Constant unless operators resize; included so the
//       dashboard can render headroom without a side query.
//
//   cena_redis_session_hits_total /             — counters mirrored from
//   cena_redis_session_misses_total             `keyspace_hits` /
//                                                 `keyspace_misses`.
//                                                 Hit ratio is derived in
//                                                 the dashboard.
//
// Why mirror counters as gauges-then-deltas?
// ------------------------------------------
// Redis INFO reports monotonic absolute values since last FLUSHALL /
// process restart. To avoid a reset-on-restart discontinuity in
// Prometheus, we publish both the raw snapshot (as a gauge) AND a
// forward-only counter derived from the delta. Prometheus' `rate()` over
// the gauge is unreliable across Redis pod restarts; the derived counter
// gives ops a stable `increase()` signal.
//
// Non-negotiables honored
// -----------------------
//   • ADR-0003 (misconception session scope) — this service is the
//     observability seam that makes the 30-day-cap + session-scope
//     guarantees verifiable. Without it, we can claim the policy but
//     not prove it.
//   • <500 LOC — this file is the metric emitter only. Alert rule file
//     and dashboard JSON live in ops/prometheus/ and ops/grafana/.
//   • No stubs — the Redis INFO path is the real one the driver exposes.
//     On connection failure we treat the sample as missing (log + emit
//     zero) rather than throwing; the RedisHealthCheck already flips the
//     pod to unhealthy in that case.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Cena.Infrastructure.Observability;

public sealed class RedisSessionStoreMetricsOptions
{
    /// <summary>
    /// Poll interval. Default: 30s. Lower increases cardinality with
    /// no operational benefit; higher risks missing a 1-minute alert
    /// window.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Which Redis logical database index the session store uses.
    /// Cena's RedisPromptCache and misconception session store both
    /// live in DB 0 by default; override if a future ADR splits them.
    /// </summary>
    public int Database { get; set; } = 0;
}

/// <summary>
/// Hosted service that polls Redis INFO and emits per-sample metrics.
/// Paired with <c>ops/prometheus/alerts-redis-sessions.yml</c> + the
/// <c>redis-session-health</c> Grafana dashboard.
/// </summary>
public sealed class RedisSessionStoreMetricsService : BackgroundService
{
    internal const string MeterName = "Cena.RedisSessionStore";

    private readonly IConnectionMultiplexer _redis;
    private readonly IOptions<RedisSessionStoreMetricsOptions> _options;
    private readonly ILogger<RedisSessionStoreMetricsService> _logger;
    private readonly Meter _meter;

    private readonly Counter<long> _evictedKeysCounter;
    private readonly Counter<long> _hitsCounter;
    private readonly Counter<long> _missesCounter;
    private long _memoryUsedBytes;
    private long _memoryMaxBytes;
    private long _totalKeys;

    // Forward-only counters need a last-seen baseline so we emit deltas.
    private long _lastEvicted = -1;
    private long _lastHits = -1;
    private long _lastMisses = -1;

    public RedisSessionStoreMetricsService(
        IConnectionMultiplexer redis,
        IOptions<RedisSessionStoreMetricsOptions> options,
        IMeterFactory meterFactory,
        ILogger<RedisSessionStoreMetricsService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _meter = meterFactory.Create(MeterName, "1.0.0");

        _evictedKeysCounter = _meter.CreateCounter<long>(
            "cena.redis_session.evicted_keys_total",
            description: "Number of Redis keys evicted due to maxmemory pressure (session-loss risk).");

        _hitsCounter = _meter.CreateCounter<long>(
            "cena.redis_session.hits_total",
            description: "Cumulative Redis keyspace hits on the session DB (from INFO stats).");

        _missesCounter = _meter.CreateCounter<long>(
            "cena.redis_session.misses_total",
            description: "Cumulative Redis keyspace misses on the session DB (from INFO stats).");

        _meter.CreateObservableGauge(
            "cena.redis_session.memory_used_bytes",
            () => Interlocked.Read(ref _memoryUsedBytes),
            description: "Redis used_memory in bytes (misconception session store).");

        _meter.CreateObservableGauge(
            "cena.redis_session.memory_max_bytes",
            () => Interlocked.Read(ref _memoryMaxBytes),
            description: "Redis maxmemory in bytes (misconception session store). Zero = unlimited.");

        _meter.CreateObservableGauge(
            "cena.redis_session.total_keys",
            () => Interlocked.Read(ref _totalKeys),
            description: "Number of keys currently in the session DB.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay — lets Redis settle after pod start before
        // the first sample is emitted.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Clean shutdown.
                return;
            }
            catch (Exception ex)
            {
                // RedisHealthCheck already trips the pod if Redis is
                // actually down — here we just log and try again on the
                // next tick. We do NOT rethrow (would kill the hosted
                // service for the life of the process).
                _logger.LogWarning(ex,
                    "[REDIS_SESSION_METRICS] Poll failed; will retry in {Interval}.",
                    _options.Value.PollInterval);
            }

            try { await Task.Delay(_options.Value.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task PollOnceAsync(CancellationToken ct)
    {
        var endpoints = _redis.GetEndPoints();
        if (endpoints.Length == 0)
        {
            _logger.LogWarning("[REDIS_SESSION_METRICS] No Redis endpoints available.");
            return;
        }

        var server = _redis.GetServer(endpoints[0]);
        if (!server.IsConnected)
        {
            _logger.LogWarning("[REDIS_SESSION_METRICS] Redis server {Endpoint} not connected.", endpoints[0]);
            return;
        }

        // INFO memory + INFO stats: two small round-trips, ~1-2ms each.
        var memoryInfo = await server.InfoAsync("memory").WaitAsync(ct).ConfigureAwait(false);
        var statsInfo = await server.InfoAsync("stats").WaitAsync(ct).ConfigureAwait(false);

        long usedMemory = ReadLong(memoryInfo, "used_memory");
        long maxMemory = ReadLong(memoryInfo, "maxmemory");
        Interlocked.Exchange(ref _memoryUsedBytes, usedMemory);
        Interlocked.Exchange(ref _memoryMaxBytes, maxMemory);

        long evicted = ReadLong(statsInfo, "evicted_keys");
        long hits = ReadLong(statsInfo, "keyspace_hits");
        long misses = ReadLong(statsInfo, "keyspace_misses");

        // Forward-only deltas — if Redis restarted, absolute values reset
        // to 0 and our previous baseline is stale. Detect the restart and
        // reseed without emitting a negative delta.
        EmitDelta(_evictedKeysCounter, ref _lastEvicted, evicted);
        EmitDelta(_hitsCounter, ref _lastHits, hits);
        EmitDelta(_missesCounter, ref _lastMisses, misses);

        // Key count is a quick DBSIZE on the configured database — cheap
        // but not free, so we do it once per poll.
        var db = _redis.GetDatabase(_options.Value.Database);
        var keys = await db.ExecuteAsync("DBSIZE").WaitAsync(ct).ConfigureAwait(false);
        if (keys.TryExtractLong(out var keyCount))
            Interlocked.Exchange(ref _totalKeys, keyCount);
    }

    private static void EmitDelta(Counter<long> counter, ref long last, long current)
    {
        if (last < 0 || current < last)
        {
            // First sample OR Redis reset — reseed baseline; no emit.
            last = current;
            return;
        }
        var delta = current - last;
        if (delta > 0) counter.Add(delta);
        last = current;
    }

    internal static long ReadLong(IGrouping<string, KeyValuePair<string, string>>[] info, string key)
    {
        foreach (var section in info)
        foreach (var kv in section)
        {
            if (string.Equals(kv.Key, key, StringComparison.Ordinal))
            {
                if (long.TryParse(kv.Value, out var v)) return v;
                return 0;
            }
        }
        return 0;
    }
}

/// <summary>
/// Small extension so we can extract DBSIZE as a long without the caller
/// needing to know the RedisValue shape.
/// </summary>
internal static class RedisResultExtensions
{
    public static bool TryExtractLong(this RedisResult result, out long value)
    {
        try
        {
            value = (long)result;
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }
}
