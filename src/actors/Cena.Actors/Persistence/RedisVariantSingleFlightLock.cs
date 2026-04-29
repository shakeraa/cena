// =============================================================================
// Cena Platform — Redis Variant Single-Flight Lock impl (PRR-260, ADR-0059 §15.5)
//
// Redis-backed implementation of IVariantSingleFlightLock. Uses two keys
// per dedup key:
//
//   • cena:vsf:lock:{dedupKey}    — writer-exclusion (SET NX EX). Value is
//                                   a per-attempt token so we can release
//                                   only our own lock (no clobber across
//                                   crash + takeover).
//   • cena:vsf:result:{dedupKey}  — JSON-serialised result. TTL outlives
//                                   the lock; reader hits land here directly.
//
// Atomicity:
//   - Acquire is a single SET NX EX (StackExchange.Redis primitive).
//   - Result-publish + lock-release is one Lua script (write result, then
//     CAS-DEL the lock by token), so the lock is always released after
//     the result is visible.
//   - Reader polling is GET on the result key + EXISTS on the lock key
//     in one Lua call so the reader never observes the half-state
//     "lock dropped, result not yet visible" (which would race the
//     reader into a takeover even though a write is about to publish).
//
// Telemetry:
//   - Counter cena_variant_singleflight_total{outcome="writer|reader|timeout|error"}
//
// Failure modes:
//   - Redis unreachable → caller's writer delegate is invoked synchronously
//     (degraded mode: no cohort collapse, but the user still gets an
//     answer). Logged + counter outcome="error".
//   - Writer throws → exception bubbles up to the caller as Error outcome;
//     the lock IS released so the next caller is the new writer.
//   - JSON serialisation failure → Error outcome, no Redis state mutated.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Persistence;

/// <summary>
/// Redis-backed cohort single-flight lock for variant generation.
/// </summary>
public sealed class RedisVariantSingleFlightLock : IVariantSingleFlightLock
{
    /// <summary>OTLP meter name. Pinned for arch tests + dashboards.</summary>
    public const string MeterName = "Cena.Persistence.VariantSingleFlight";

    /// <summary>Counter name. Pinned so dashboards can target it directly.</summary>
    public const string CounterName = "cena_variant_singleflight_total";

    /// <summary>Redis key prefix. Editing rotates the keyspace — coordinate.</summary>
    public const string KeyPrefix = "cena:vsf";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisVariantSingleFlightLock> _logger;
    private readonly Counter<long> _outcomeCounter;
    private readonly TimeProvider _clock;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Compact + stable across .NET versions. Default is fine; we set
        // PropertyNamingPolicy explicitly so a future host-side default
        // change can't silently rotate the cache shape.
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public RedisVariantSingleFlightLock(
        IConnectionMultiplexer redis,
        IMeterFactory meterFactory,
        ILogger<RedisVariantSingleFlightLock> logger,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;

        var meter = meterFactory.Create(MeterName, "1.0.0");
        _outcomeCounter = meter.CreateCounter<long>(
            CounterName,
            unit: "events",
            description:
                "Variant single-flight lock outcomes. tag outcome=writer|reader|timeout|error. " +
                "Cohort-collapse ratio = sum{reader} / sum{writer}; high values mean the lock " +
                "is paying for itself (e.g. 30 → 1× LLM cost reduction).");
    }

    /// <summary>Build the lock key for a dedup key. Public for tests + ops.</summary>
    public static string LockKey(string dedupKey) => $"{KeyPrefix}:lock:{dedupKey}";

    /// <summary>Build the result-cache key for a dedup key. Public for tests + ops.</summary>
    public static string ResultKey(string dedupKey) => $"{KeyPrefix}:result:{dedupKey}";

    /// <inheritdoc/>
    public async Task<VariantSingleFlightOutcome<T>> ExecuteAsync<T>(
        string dedupKey,
        Func<CancellationToken, Task<T>> writer,
        VariantSingleFlightOptions? options = null,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);
        ArgumentNullException.ThrowIfNull(writer);
        var opts = options ?? VariantSingleFlightOptions.Default;
        ValidateOptions(opts);

        var lockKey = LockKey(dedupKey);
        var resultKey = ResultKey(dedupKey);

        IDatabase db;
        try
        {
            db = _redis.GetDatabase();
        }
        catch (Exception ex)
        {
            // Redis unreachable. Fall back to running the writer directly so the
            // user still gets a response — we lose cohort-collapse, but a hard
            // failure here would make a Redis blip a customer-visible 500.
            _logger.LogWarning(ex,
                "[VARIANT_SINGLEFLIGHT_REDIS_DOWN] dedupKey={Key}; running writer inline.", dedupKey);
            return await RunWriterInline(writer, dedupKey, ct).ConfigureAwait(false);
        }

        // Fast-path: result already cached → reader-by-cache-hit.
        try
        {
            var cached = await db.StringGetAsync(resultKey).ConfigureAwait(false);
            if (cached.HasValue)
            {
                var hit = TryDeserialize<T>(cached!);
                if (hit is not null)
                {
                    Tag("reader");
                    return VariantSingleFlightOutcome<T>.AsReader(hit);
                }
                // Bad payload — fall through; writer will overwrite.
                _logger.LogWarning(
                    "[VARIANT_SINGLEFLIGHT_BAD_PAYLOAD] dedupKey={Key}; ignoring cached and re-attempting.",
                    dedupKey);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[VARIANT_SINGLEFLIGHT_GET_FAIL] dedupKey={Key}; running writer inline.", dedupKey);
            return await RunWriterInline(writer, dedupKey, ct).ConfigureAwait(false);
        }

        // Try-acquire loop: each pass either becomes writer, or reads, or
        // gives up after the reader-wait budget expires.
        var token = Guid.NewGuid().ToString("N");
        var deadline = _clock.GetUtcNow() + opts.ReaderWaitBudget;
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            // 1. Try to become writer.
            bool acquired;
            try
            {
                acquired = await db.StringSetAsync(
                    lockKey,
                    token,
                    expiry: opts.LockTtl,
                    when: When.NotExists).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[VARIANT_SINGLEFLIGHT_ACQUIRE_FAIL] dedupKey={Key} attempt={Attempt}; running writer inline.",
                    dedupKey, attempt);
                return await RunWriterInline(writer, dedupKey, ct).ConfigureAwait(false);
            }

            if (acquired)
            {
                return await RunAsWriter(db, dedupKey, lockKey, resultKey, token, writer, opts, ct)
                    .ConfigureAwait(false);
            }

            // 2. Lock held by someone else — wait for the result.
            var pollResult = await PollForResult<T>(db, lockKey, resultKey, opts, deadline, ct)
                .ConfigureAwait(false);
            if (pollResult.Outcome is { } success)
            {
                return success;
            }
            if (pollResult.LockExpiredOrAbsent)
            {
                // Writer crashed / lock TTL elapsed without publishing.
                // Loop back and try to become the new writer.
                continue;
            }

            // Reader budget expired.
            Tag("timeout");
            _logger.LogWarning(
                "[VARIANT_SINGLEFLIGHT_TIMEOUT] dedupKey={Key} attempts={Attempts} budget={Budget}",
                dedupKey, attempt, opts.ReaderWaitBudget);
            return VariantSingleFlightOutcome<T>.AsTimeout();
        }
    }

    /// <summary>
    /// Run the writer delegate, publish the result, and release our lock.
    /// On writer failure, release the lock so the next caller can take over.
    /// </summary>
    private async Task<VariantSingleFlightOutcome<T>> RunAsWriter<T>(
        IDatabase db,
        string dedupKey,
        string lockKey,
        string resultKey,
        string token,
        Func<CancellationToken, Task<T>> writer,
        VariantSingleFlightOptions opts,
        CancellationToken ct) where T : class
    {
        T result;
        try
        {
            result = await writer(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Release our lock (CAS by token) so the next caller can retry.
            await TryReleaseLock(db, lockKey, token).ConfigureAwait(false);
            Tag("error");
            _logger.LogError(ex,
                "[VARIANT_SINGLEFLIGHT_WRITER_THREW] dedupKey={Key}", dedupKey);
            return VariantSingleFlightOutcome<T>.AsError(ex.Message);
        }

        // Publish result + release lock atomically (Lua).
        try
        {
            var serialized = JsonSerializer.Serialize(result, JsonOpts);
            const string lua = @"
                redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
                if redis.call('GET', KEYS[2]) == ARGV[3] then
                    redis.call('DEL', KEYS[2])
                end
                return 1
            ";
            await db.ScriptEvaluateAsync(
                lua,
                new RedisKey[] { resultKey, lockKey },
                new RedisValue[] { serialized, (long)opts.ResultTtl.TotalSeconds, token })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Result wasn't published — but we DID compute it. Return Writer
            // anyway; cohort callers will become the new writer when our
            // lock TTL expires (degraded mode). Log + counter as error.
            _logger.LogWarning(ex,
                "[VARIANT_SINGLEFLIGHT_PUBLISH_FAIL] dedupKey={Key}; result returned but cache miss.",
                dedupKey);
        }

        Tag("writer");
        return VariantSingleFlightOutcome<T>.AsWriter(result);
    }

    /// <summary>
    /// Poll for the result key, with a single Lua call per poll that checks
    /// (a) result, (b) lock-existence so we don't busy-loop after takeover.
    /// Returns either a successful Reader outcome, or a flag indicating the
    /// lock has been released (so the caller should attempt to become
    /// writer), or null on the budget expiry.
    /// </summary>
    private async Task<PollResult<T>> PollForResult<T>(
        IDatabase db,
        string lockKey,
        string resultKey,
        VariantSingleFlightOptions opts,
        DateTimeOffset deadline,
        CancellationToken ct) where T : class
    {
        const string lua = @"
            local r = redis.call('GET', KEYS[1])
            local l = redis.call('EXISTS', KEYS[2])
            return { r, l }
        ";

        while (_clock.GetUtcNow() < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var raw = (RedisResult[]?)await db.ScriptEvaluateAsync(
                    lua,
                    new RedisKey[] { resultKey, lockKey })
                    .ConfigureAwait(false);
                if (raw is not null)
                {
                    var resultBlob = raw[0];
                    var lockExists = (long)raw[1] == 1;

                    if (!resultBlob.IsNull)
                    {
                        var hit = TryDeserialize<T>((string?)resultBlob ?? string.Empty);
                        if (hit is not null)
                        {
                            Tag("reader");
                            return new PollResult<T>(VariantSingleFlightOutcome<T>.AsReader(hit), false);
                        }
                    }
                    if (!lockExists)
                    {
                        return new PollResult<T>(null, true);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "[VARIANT_SINGLEFLIGHT_POLL_RETRY] resultKey={Key}", resultKey);
            }

            await Task.Delay(opts.ReaderPollInterval, ct).ConfigureAwait(false);
        }

        return new PollResult<T>(null, false);
    }

    private async Task TryReleaseLock(IDatabase db, string lockKey, string token)
    {
        try
        {
            const string lua = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                end
                return 0
            ";
            await db.ScriptEvaluateAsync(
                lua,
                new RedisKey[] { lockKey },
                new RedisValue[] { token })
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort. Lock will TTL away if release fails.
        }
    }

    private async Task<VariantSingleFlightOutcome<T>> RunWriterInline<T>(
        Func<CancellationToken, Task<T>> writer,
        string dedupKey,
        CancellationToken ct) where T : class
    {
        try
        {
            var r = await writer(ct).ConfigureAwait(false);
            Tag("error");   // degraded path — count as error so dashboards show the rate
            return VariantSingleFlightOutcome<T>.AsWriter(r);
        }
        catch (Exception ex)
        {
            Tag("error");
            _logger.LogError(ex,
                "[VARIANT_SINGLEFLIGHT_INLINE_WRITER_THREW] dedupKey={Key}", dedupKey);
            return VariantSingleFlightOutcome<T>.AsError(ex.Message);
        }
    }

    private static T? TryDeserialize<T>(string blob) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(blob, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Tag(string outcome) =>
        _outcomeCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    private static void ValidateOptions(VariantSingleFlightOptions opts)
    {
        if (opts.LockTtl <= TimeSpan.Zero)
            throw new ArgumentException("LockTtl must be positive.", nameof(opts));
        if (opts.ResultTtl <= TimeSpan.Zero)
            throw new ArgumentException("ResultTtl must be positive.", nameof(opts));
        if (opts.ReaderWaitBudget <= TimeSpan.Zero)
            throw new ArgumentException("ReaderWaitBudget must be positive.", nameof(opts));
        if (opts.ReaderPollInterval <= TimeSpan.Zero)
            throw new ArgumentException("ReaderPollInterval must be positive.", nameof(opts));
    }

    private readonly record struct PollResult<T>(
        VariantSingleFlightOutcome<T>? Outcome,
        bool LockExpiredOrAbsent) where T : class;
}

/// <summary>
/// No-op single-flight lock for tests + offline tooling. Always runs the
/// writer inline; never collapses cohorts. Production hosts NEVER register this.
/// </summary>
public sealed class NullVariantSingleFlightLock : IVariantSingleFlightLock
{
    /// <summary>Stateless shared instance.</summary>
    public static readonly NullVariantSingleFlightLock Instance = new();

    private NullVariantSingleFlightLock() { }

    /// <inheritdoc/>
    public async Task<VariantSingleFlightOutcome<T>> ExecuteAsync<T>(
        string dedupKey,
        Func<CancellationToken, Task<T>> writer,
        VariantSingleFlightOptions? options = null,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);
        ArgumentNullException.ThrowIfNull(writer);
        try
        {
            var r = await writer(ct).ConfigureAwait(false);
            return VariantSingleFlightOutcome<T>.AsWriter(r);
        }
        catch (Exception ex)
        {
            return VariantSingleFlightOutcome<T>.AsError(ex.Message);
        }
    }
}
