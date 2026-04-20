// =============================================================================
// Cena Platform -- GDPR Right to Erasure Worker
// SEC-005: Background service processing erasure requests after cooling period
//
// Runs on configurable schedule (default: daily at 3 AM), processes erasure
// requests that have passed their 30-day cooling period, and emits audit events.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Infrastructure.Compliance.KeyStore;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Configuration options for the erasure worker.
/// </summary>
public sealed class ErasureWorkerOptions
{
    /// <summary>Cron expression for scheduling (default: 0 3 * * * = daily at 3 AM).</summary>
    public string CronExpression { get; set; } = "0 3 * * *";

    /// <summary>Cooling period before erasure can be processed (default: 30 days).</summary>
    public TimeSpan CoolingPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Batch size for processing requests per run.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Timeout for the entire erasure run.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Background service that processes GDPR erasure requests after their cooling period.
/// </summary>
public sealed class ErasureWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ErasureWorker> _logger;
    private readonly ErasureWorkerOptions _options;
    private readonly IClock _clock;
    private CrontabSchedule? _schedule;
    private DateTime _nextRunTime;

    // Metrics
    private static readonly Meter Meter = new("Cena.Compliance.Erasure", "1.0.0");
    private static readonly Counter<long> ProcessedCounter = Meter.CreateCounter<long>("cena.erasure.processed_total");
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("cena.erasure.failed_total");
    private static readonly Histogram<double> DurationHistogram = Meter.CreateHistogram<double>("cena.erasure.duration_seconds");

    public ErasureWorker(
        IServiceProvider serviceProvider,
        ILogger<ErasureWorker> logger,
        IOptions<ErasureWorkerOptions> options,
        IClock clock)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Parse cron schedule
        _schedule = CrontabSchedule.Parse(_options.CronExpression, new CrontabSchedule.ParseOptions { IncludingSeconds = false });
        _nextRunTime = _schedule.GetNextOccurrence(_clock.UtcNow.DateTime);

        _logger.LogInformation("[SIEM] ErasureWorker started. Schedule: {Cron}, Next run: {NextRun}, CoolingPeriod: {CoolingDays} days",
            _options.CronExpression, _nextRunTime, _options.CoolingPeriod.TotalDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _clock.UtcNow;
            var delay = _nextRunTime - now;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug("ErasureWorker sleeping until {NextRun} ({Delay:hh\\:mm\\:ss})", _nextRunTime, delay);
                await Task.Delay(delay, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunErasureAsync(stoppingToken);

            // Schedule next run
            _nextRunTime = _schedule.GetNextOccurrence(_clock.UtcNow.DateTime);
        }
    }

    private async Task RunErasureAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var runId = Guid.NewGuid();
        var runAt = _clock.UtcNow;

        _logger.LogInformation("[SIEM] ErasureWorkerRun: {RunId} started at {RunAt}", runId, runAt);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var erasureService = scope.ServiceProvider.GetRequiredService<IRightToErasureService>();
        var subjectKeyStore = scope.ServiceProvider.GetService<ISubjectKeyStore>();

        int processedCount = 0;
        int failedCount = 0;
        List<ErasureProcessResult> results = new();

        try
        {
            // Query for erasure requests in cooling period
            var eligibleRequests = await GetEligibleErasureRequestsAsync(store, ct);

            _logger.LogInformation("[SIEM] ErasureWorkerRun: {RunId} found {Count} eligible requests for processing",
                runId, eligibleRequests.Count);

            // Process each eligible request
            foreach (var request in eligibleRequests.Take(_options.BatchSize))
            {
                var result = await ProcessSingleRequestAsync(erasureService, request, ct);
                results.Add(result);

                // ADR-0038 (crypto-shredding): regardless of projection-side
                // anonymisation outcomes, destroy the subject key so every
                // past and future ciphertext in the append-only event store
                // becomes undecryptable. Audit-only hash of subject-id — we
                // never log the raw subject-id here per ADR §"Audit trail".
                if (result.Success && subjectKeyStore is not null)
                {
                    try
                    {
                        var wasAlive = await subjectKeyStore.DeleteAsync(request.StudentId, ct);
                        _logger.LogInformation(
                            "[SIEM] ErasureSubjectKeyTombstoned: SubjectIdHash={Hash}, priorExisted={Existed}, "
                            + "authSource={AuthSource}, at={At}, RunId={RunId}",
                            InMemorySubjectKeyStore.HashSubjectForLog(request.StudentId),
                            wasAlive,
                            "data-subject-request",
                            _clock.UtcNow,
                            runId);
                    }
                    catch (Exception keyEx)
                    {
                        _logger.LogError(keyEx,
                            "[SIEM] ErasureSubjectKeyTombstoneFailed: SubjectIdHash={Hash}, RunId={RunId}",
                            InMemorySubjectKeyStore.HashSubjectForLog(request.StudentId), runId);
                    }
                }

                if (result.Success)
                {
                    processedCount++;
                    _logger.LogInformation(
                        "[SIEM] ErasureRequestProcessed: {RequestId}, StudentId={StudentId}, DurationMs={DurationMs}, RunId={RunId}",
                        request.Id, request.StudentId, result.DurationMs, runId);
                }
                else
                {
                    failedCount++;
                    _logger.LogError(result.Exception,
                        "[SIEM] ErasureRequestFailed: {RequestId}, StudentId={StudentId}, Error={Error}, RunId={RunId}",
                        request.Id, request.StudentId, result.ErrorMessage, runId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SIEM] ErasureWorkerRun: {RunId} failed with unhandled exception: {Error}", runId, ex.Message);
            FailedCounter.Add(1);
        }

        sw.Stop();

        // Record metrics
        ProcessedCounter.Add(processedCount);
        FailedCounter.Add(failedCount);
        DurationHistogram.Record(sw.Elapsed.TotalSeconds);

        // Emit structured log for SIEM
        _logger.LogInformation(
            "[SIEM] ErasureWorkerRunCompleted_V1: {RunId}, Status={Status}, DurationMs={DurationMs}, " +
            "Processed={Processed}, Failed={Failed}, TotalEligible={TotalEligible}",
            runId, failedCount == 0 ? "Completed" : "PartialFailure", sw.ElapsedMilliseconds,
            processedCount, failedCount, results.Count);
    }

    private async Task<IReadOnlyList<ErasureRequest>> GetEligibleErasureRequestsAsync(IDocumentStore store, CancellationToken ct)
    {
        var cutoffTime = _clock.UtcNow - _options.CoolingPeriod;

        await using var session = store.QuerySession();

        var requests = await session.Query<ErasureRequest>()
            .Where(x => x.Status == ErasureStatus.CoolingPeriod)
            .Where(x => x.RequestedAt <= cutoffTime)
            .ToListAsync(ct);

        return requests;
    }

    private async Task<ErasureProcessResult> ProcessSingleRequestAsync(
        IRightToErasureService erasureService,
        ErasureRequest request,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new ErasureProcessResult
        {
            RequestId = request.Id,
            StudentId = request.StudentId
        };

        try
        {
            await erasureService.ProcessErasureAsync(request.StudentId, ct);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
        }

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;

        return result;
    }
}

/// <summary>
/// Result of processing a single erasure request.
/// </summary>
public sealed class ErasureProcessResult
{
    public Guid RequestId { get; set; }
    public string StudentId { get; set; } = "";
    public bool Success { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// Extension methods for registering the erasure worker.
/// </summary>
public static class ErasureWorkerExtensions
{
    /// <summary>
    /// Adds the ErasureWorker hosted service and related services to the DI container.
    /// </summary>
    public static IServiceCollection AddErasureWorker(
        this IServiceCollection services,
        Action<ErasureWorkerOptions>? configure = null)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddHostedService<ErasureWorker>();

        // ADR-0038: crypto-shredding primitives. Idempotent (TryAdd inside).
        services.AddSubjectKeyStore();

        if (configure != null)
            services.Configure(configure);
        else
            services.Configure<ErasureWorkerOptions>(_ => { });

        return services;
    }
}

/// <summary>
/// Abstraction for time to enable deterministic time-based testing.
/// Production uses UtcNow; tests can fast-forward time.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Production clock implementation using system time.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
