// =============================================================================
// Cena Platform — Misconception PII Retention Worker (prr-015, ADR-0003)
//
// Background service that iterates every entry in
// IMisconceptionPiiStoreRegistry nightly and invokes the store's declared
// purge callback with a retention cutoff clamped to the 90-day hard cap.
//
// Why a dedicated worker?
//
//   The existing RetentionWorker handles the canonical Marten event stream
//   and a fixed set of category documents. prr-015 extends the model to
//   cover EVERY secondary misconception store (projections, caches, Redis,
//   in-memory replicas). Putting the extension points into the registry
//   rather than hard-coding new cases into RetentionWorker keeps that worker
//   under its 500-LOC baseline AND gives reviewers a single file to inspect
//   when auditing "who holds misconception PII".
//
// Metrics (Meter "Cena.Compliance.MisconceptionRetention"):
//
//   cena_misconception_store_registered_total{store} — counter of
//       registered stores at startup (one increment per Register call).
//
//   cena_misconception_retention_purge_total{store,reason} — counter of
//       records purged per store per run. `reason` ∈ { "aged-out",
//       "clamped-to-hard-cap", "failure" }.
//
//   cena_misconception_retention_lag_hours{store} — observable gauge:
//       hours since the store's last successful purge. Alerts >48h.
//
// Non-negotiables honored:
//
//   • No file > 500 LOC (this file stays under the baseline).
//   • Purge callbacks are per-store — the worker contains no hard-coded
//     knowledge of which docs live in which store.
//   • 30-day default retention, 90-day hard cap (ADR-0003 Decision 2).
//   • Failures in one store do not abort the whole run (other stores still
//     purge; failures are visible via the purge counter with reason=failure).
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Configuration for the misconception-specific retention worker. Kept
/// separate from <see cref="RetentionWorkerOptions"/> so hosts can schedule
/// the two jobs independently (the misconception sweep is cheaper and can
/// run more often, e.g. every 6 hours vs. nightly).
/// </summary>
public sealed class MisconceptionRetentionWorkerOptions
{
    /// <summary>Cron expression (default: hourly at :15).</summary>
    public string CronExpression { get; set; } = "15 * * * *";

    /// <summary>Per-store timeout. A slow store does not starve the others.</summary>
    public TimeSpan StoreTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// IHostedService that drives <see cref="IMisconceptionPiiStoreRegistry"/>
/// purges on a cron schedule.
/// </summary>
public sealed class MisconceptionRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MisconceptionRetentionWorker> _logger;
    private readonly MisconceptionRetentionWorkerOptions _options;
    private readonly IClock _clock;
    private readonly MisconceptionRetentionMetrics _metrics;
    private CrontabSchedule? _schedule;

    public MisconceptionRetentionWorker(
        IServiceProvider serviceProvider,
        ILogger<MisconceptionRetentionWorker> logger,
        IOptions<MisconceptionRetentionWorkerOptions> options,
        IClock clock,
        MisconceptionRetentionMetrics metrics)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _clock = clock;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _schedule = CrontabSchedule.Parse(
            _options.CronExpression,
            new CrontabSchedule.ParseOptions { IncludingSeconds = false });

        _logger.LogInformation(
            "[SIEM] MisconceptionRetentionWorker started. Schedule: {Cron}",
            _options.CronExpression);

        var nextRun = _schedule.GetNextOccurrence(_clock.UtcNow.DateTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = nextRun - _clock.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            nextRun = _schedule.GetNextOccurrence(_clock.UtcNow.DateTime);
        }
    }

    /// <summary>
    /// One full sweep across every registered store. Exposed for tests
    /// (call directly with a TestClock). Never throws — all per-store
    /// failures are caught, logged, and counted.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var registry = scope.ServiceProvider
            .GetRequiredService<IMisconceptionPiiStoreRegistry>();

        var stores = registry.GetAll();
        _logger.LogInformation(
            "[SIEM] MisconceptionRetentionRunStarted: stores={Count}, at={RunAt}",
            stores.Count, _clock.UtcNow);

        foreach (var store in stores)
        {
            ct.ThrowIfCancellationRequested();
            await PurgeStoreAsync(registry, store, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "[SIEM] MisconceptionRetentionRunCompleted: stores={Count}",
            stores.Count);
    }

    private async Task PurgeStoreAsync(
        IMisconceptionPiiStoreRegistry registry,
        RegisteredMisconceptionStore store,
        CancellationToken ct)
    {
        var declaredDays = store.RetentionDays;
        var effectiveDays = store.EffectiveRetentionDays;
        if (declaredDays != effectiveDays)
        {
            _logger.LogWarning(
                "[SIEM] MisconceptionStoreRetentionClamped: store={Store}, declared={Declared}d, effective={Effective}d (ADR-0003 hard cap)",
                store.StoreName, declaredDays, effectiveDays);
            _metrics.RecordPurge(store.StoreName, reason: "clamped-to-hard-cap", count: 0);
        }

        var cutoff = _clock.UtcNow - TimeSpan.FromDays(effectiveDays);
        var callback = registry.GetPurgeCallback(store.StoreName);

        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.StoreTimeout);

        try
        {
            var purged = await callback(cutoff, cts.Token).ConfigureAwait(false);
            sw.Stop();

            _metrics.RecordPurge(store.StoreName, reason: "aged-out", count: purged);
            _metrics.MarkSuccess(store.StoreName, _clock.UtcNow);

            _logger.LogInformation(
                "[SIEM] MisconceptionStorePurged: store={Store}, purged={Count}, cutoff={Cutoff}, durationMs={DurationMs}",
                store.StoreName, purged, cutoff, sw.ElapsedMilliseconds);

            if (!store.SessionScopeVerified)
            {
                // Non-fatal: purge still ran, but the store is not
                // verified as session-scoped. That is itself an audit
                // finding — surface it loudly.
                _logger.LogWarning(
                    "[SIEM] MisconceptionStoreSessionScopeUnverified: store={Store}. " +
                    "Register with session_scope_verified=true once an architect has " +
                    "confirmed the store cannot hold profile-scoped misconception data.",
                    store.StoreName);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            _metrics.RecordPurge(store.StoreName, reason: "failure", count: 0);
            _logger.LogError(
                "[SIEM] MisconceptionStorePurgeTimeout: store={Store}, timeoutMs={TimeoutMs}",
                store.StoreName, _options.StoreTimeout.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _metrics.RecordPurge(store.StoreName, reason: "failure", count: 0);
            _logger.LogError(ex,
                "[SIEM] MisconceptionStorePurgeFailed: store={Store}, durationMs={DurationMs}",
                store.StoreName, sw.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// Meter + counters + observable gauge for the misconception retention
/// sweep. Isolated from the worker so tests can observe without spinning
/// up the hosted service.
/// </summary>
public sealed class MisconceptionRetentionMetrics : IDisposable
{
    private readonly Meter _meter = new("Cena.Compliance.MisconceptionRetention", "1.0.0");
    private readonly Counter<long> _registered;
    private readonly Counter<long> _purge;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSuccess = new(StringComparer.Ordinal);
    private readonly IClock _clock;

    public MisconceptionRetentionMetrics(IClock clock)
    {
        _clock = clock;
        _registered = _meter.CreateCounter<long>(
            "cena_misconception_store_registered_total",
            description: "Count of misconception PII stores registered at startup.");
        _purge = _meter.CreateCounter<long>(
            "cena_misconception_retention_purge_total",
            description: "Count of misconception PII records purged per store per run.");
        _meter.CreateObservableGauge(
            "cena_misconception_retention_lag_hours",
            ObserveLagHours,
            description: "Hours since the store's last successful purge. Alert when > 48.");
    }

    /// <summary>Increment the registration counter for a newly registered store.</summary>
    public void RecordRegistration(string storeName)
    {
        _registered.Add(1, new KeyValuePair<string, object?>("store", storeName));
    }

    /// <summary>Record a purge event for a store with the given reason + count.</summary>
    public void RecordPurge(string storeName, string reason, int count)
    {
        _purge.Add(
            count,
            new KeyValuePair<string, object?>("store", storeName),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>Mark a store as having successfully purged at the given time.</summary>
    public void MarkSuccess(string storeName, DateTimeOffset at)
    {
        _lastSuccess[storeName] = at;
    }

    /// <summary>Test hook: read the last-success timestamp for a store.</summary>
    public DateTimeOffset? GetLastSuccess(string storeName)
        => _lastSuccess.TryGetValue(storeName, out var t) ? t : null;

    private IEnumerable<Measurement<double>> ObserveLagHours()
    {
        var now = _clock.UtcNow;
        foreach (var kv in _lastSuccess)
        {
            var lag = (now - kv.Value).TotalHours;
            yield return new Measurement<double>(
                lag,
                new KeyValuePair<string, object?>("store", kv.Key));
        }
    }

    public void Dispose() => _meter.Dispose();
}

/// <summary>
/// DI extensions to wire the registry, metrics, and hosted worker.
/// </summary>
public static class MisconceptionPiiRegistryServiceCollectionExtensions
{
    /// <summary>
    /// Register the in-memory registry, the metrics singleton, and the
    /// hosted <see cref="MisconceptionRetentionWorker"/>. Idempotent —
    /// calling twice yields a single registry singleton.
    /// </summary>
    public static IServiceCollection AddMisconceptionPiiStoreRegistry(
        this IServiceCollection services,
        Action<MisconceptionRetentionWorkerOptions>? configure = null)
    {
        services.TryAddSingleton<IMisconceptionPiiStoreRegistry, InMemoryMisconceptionPiiStoreRegistry>();
        services.TryAddSingleton<MisconceptionRetentionMetrics>();
        // IClock is registered by AddRetentionWorker in the host composition;
        // we TryAdd here so this extension is usable in isolation.
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddHostedService<MisconceptionRetentionWorker>();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<MisconceptionRetentionWorkerOptions>(_ => { });

        return services;
    }

    /// <summary>
    /// Convenience overload that registers a store at DI time and wires
    /// the registration-counter metric. Prefer this over calling
    /// <see cref="IMisconceptionPiiStoreRegistry.Register"/> directly from
    /// a host startup script so the counter stays in sync.
    /// </summary>
    public static IServiceCollection RegisterMisconceptionPiiStore(
        this IServiceCollection services,
        RegisteredMisconceptionStore store,
        Func<IServiceProvider, MisconceptionPurgeCallback> callbackFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(callbackFactory);
        store.AssertValid();

        services.AddSingleton<IHostedService>(sp =>
            new MisconceptionStoreBootstrapper(
                sp.GetRequiredService<IMisconceptionPiiStoreRegistry>(),
                sp.GetRequiredService<MisconceptionRetentionMetrics>(),
                store,
                callbackFactory(sp)));

        return services;
    }
}

/// <summary>
/// Tiny hosted service whose only job is to register a store + bump the
/// metric on startup. Running as IHostedService (rather than at DI
/// construction time) guarantees ordering: the registry is built,
/// MisconceptionRetentionWorker has not started yet (it sleeps until the
/// first cron tick), and all registrations land before the first sweep.
/// </summary>
internal sealed class MisconceptionStoreBootstrapper : IHostedService
{
    private readonly IMisconceptionPiiStoreRegistry _registry;
    private readonly MisconceptionRetentionMetrics _metrics;
    private readonly RegisteredMisconceptionStore _store;
    private readonly MisconceptionPurgeCallback _callback;

    public MisconceptionStoreBootstrapper(
        IMisconceptionPiiStoreRegistry registry,
        MisconceptionRetentionMetrics metrics,
        RegisteredMisconceptionStore store,
        MisconceptionPurgeCallback callback)
    {
        _registry = registry;
        _metrics = metrics;
        _store = store;
        _callback = callback;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Register(_store, _callback);
        _metrics.RecordRegistration(_store.StoreName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
