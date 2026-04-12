// =============================================================================
// Cena Platform -- Data Retention Worker
// REV-013.3: Background service enforcing GDPR/FERPA/COPPA data retention policies
//
// Runs on configurable schedule (default: daily at 2 AM), purges expired documents
// per category, accelerates erasure requests, and emits audit events.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Configuration options for the retention worker.
/// </summary>
public sealed class RetentionWorkerOptions
{
    /// <summary>Cron expression for scheduling (default: 0 2 * * * = daily at 2 AM).</summary>
    public string CronExpression { get; set; } = "0 2 * * *";

    /// <summary>Use soft-delete (Marten) where possible; event streams always hard-deleted.</summary>
    public bool UseSoftDelete { get; set; } = true;

    /// <summary>Batch size for deletion queries.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Timeout per category operation.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Delay between batches to prevent memory pressure.</summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Background service that enforces data retention policies.
/// </summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetentionWorker> _logger;
    private readonly RetentionWorkerOptions _options;
    private readonly IClock _clock;
    private CrontabSchedule? _schedule;
    private DateTime _nextRunTime;

    // Metrics
    private static readonly Meter Meter = new("Cena.Compliance.Retention", "1.0.0");
    private static readonly Counter<long> RunsCounter = Meter.CreateCounter<long>("cena.retention.runs_total");
    private static readonly Counter<long> RowsPurgedCounter = Meter.CreateCounter<long>("cena.retention.rows_purged_total");
    private static readonly Counter<long> FailuresCounter = Meter.CreateCounter<long>("cena.retention.failures_total");
    private static readonly Histogram<double> DurationHistogram = Meter.CreateHistogram<double>("cena.retention.duration_seconds");

    public RetentionWorker(
        IServiceProvider serviceProvider,
        ILogger<RetentionWorker> logger,
        IOptions<RetentionWorkerOptions> options,
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

        _logger.LogInformation("[SIEM] RetentionWorker started. Schedule: {Cron}, Next run: {NextRun}",
            _options.CronExpression, _nextRunTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _clock.UtcNow;
            var delay = _nextRunTime - now;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug("RetentionWorker sleeping until {NextRun} ({Delay:hh\\:mm\\:ss})", _nextRunTime, delay);
                await Task.Delay(delay, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunRetentionAsync(stoppingToken);

            // Schedule next run
            _nextRunTime = _schedule.GetNextOccurrence(_clock.UtcNow.DateTime);
        }
    }

    private async Task RunRetentionAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var runId = Guid.NewGuid();
        var runAt = _clock.UtcNow;

        _logger.LogInformation("[SIEM] RetentionRunStarted: {RunId} at {RunAt}", runId, runAt);
        RunsCounter.Add(1);

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var policyService = scope.ServiceProvider.GetService<IRetentionPolicyService>();

        var history = new RetentionRunHistory
        {
            Id = runId,
            RunAt = runAt,
            Status = RetentionRunStatus.Running
        };

        await using (var session = store.LightweightSession())
        {
            session.Store(history);
            await session.SaveChangesAsync(ct);
        }

        var summaries = new List<RetentionCategorySummary>();
        int totalPurged = 0;
        int totalErasureAccelerated = 0;
        Exception? failure = null;

        try
        {
            // Process each retention category
            var categories = new[]
            {
                (Category: DataCategory.StudentRecord, Name: "Student Education Records", DefaultRetention: DataRetentionPolicy.StudentRecordRetention),
                (Category: DataCategory.AuditLog, Name: "Audit Logs", DefaultRetention: DataRetentionPolicy.AuditLogRetention),
                (Category: DataCategory.Analytics, Name: "Session Analytics", DefaultRetention: DataRetentionPolicy.AnalyticsRetention),
                (Category: DataCategory.Engagement, Name: "Engagement Data", DefaultRetention: DataRetentionPolicy.EngagementRetention)
            };

            foreach (var (category, name, defaultRetention) in categories)
            {
                var retention = await GetRetentionForCategoryAsync(policyService, category, defaultRetention, ct);
                var summary = await ProcessCategoryAsync(store, category, name, retention, ct);
                summaries.Add(summary);
                totalPurged += summary.PurgedCount;
            }

            // Accelerate erasure requests
            totalErasureAccelerated = await AccelerateErasureRequestsAsync(store, ct);

            history.Status = RetentionRunStatus.Completed;
        }
        catch (Exception ex)
        {
            failure = ex;
            history.Status = RetentionRunStatus.Failed;
            history.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[SIEM] RetentionRunFailed: {RunId} - {Error}", runId, ex.Message);
            FailuresCounter.Add(1);
        }

        sw.Stop();
        history.CompletedAt = _clock.UtcNow;
        history.DocumentsPurged = totalPurged;
        history.ErasureRequestsAccelerated = totalErasureAccelerated;
        history.CategorySummaries = summaries;

        await using (var session = store.LightweightSession())
        {
            session.Store(history);
            await session.SaveChangesAsync(ct);
        }

        // Emit structured log for SIEM
        _logger.LogInformation(
            "[SIEM] RetentionRunCompleted_V1: {RunId}, Status={Status}, DurationMs={DurationMs}, " +
            "Purged={Purged}, ErasureAccelerated={Erasure}, Categories={Categories}",
            runId, history.Status, sw.ElapsedMilliseconds, totalPurged, totalErasureAccelerated, summaries.Count);

        DurationHistogram.Record(sw.Elapsed.TotalSeconds);
        RowsPurgedCounter.Add(totalPurged);
    }

    private async Task<TimeSpan> GetRetentionForCategoryAsync(
        IRetentionPolicyService? policyService,
        DataCategory category,
        TimeSpan defaultRetention,
        CancellationToken ct)
    {
        if (policyService == null) return defaultRetention;
        try
        {
            // Note: This would need tenant-aware lookup in full implementation
            return await policyService.GetRetentionPeriodAsync("global", category, ct);
        }
        catch
        {
            return defaultRetention;
        }
    }

    private async Task<RetentionCategorySummary> ProcessCategoryAsync(
        IDocumentStore store,
        DataCategory category,
        string name,
        TimeSpan retention,
        CancellationToken ct)
    {
        var summary = new RetentionCategorySummary
        {
            Category = name,
            RetentionPeriod = retention
        };

        var cutoff = _clock.UtcNow - retention;
        _logger.LogDebug("Processing {Category}: retention={Retention}, cutoff={Cutoff}", name, retention, cutoff);

        try
        {
            await using var session = store.LightweightSession();

            switch (category)
            {
                case DataCategory.AuditLog:
                    await PurgeAuditLogsAsync(session, cutoff, summary, ct);
                    break;

                case DataCategory.Analytics:
                    await PurgeAnalyticsAsync(session, cutoff, summary, ct);
                    break;

                case DataCategory.Engagement:
                    await PurgeEngagementAsync(session, cutoff, summary, ct);
                    break;

                case DataCategory.StudentRecord:
                    // Student records require special handling - archive before delete
                    await ArchiveAndPurgeStudentRecordsAsync(session, cutoff, summary, ct);
                    break;
            }

            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process category {Category}", name);
            throw;
        }

        return summary;
    }

    private async Task PurgeAuditLogsAsync(
        IDocumentSession session,
        DateTimeOffset cutoff,
        RetentionCategorySummary summary,
        CancellationToken ct)
    {
        // Query expired audit logs
        var expired = await session.Query<StudentRecordAccessLog>()
            .Where(x => x.AccessedAt < cutoff)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        summary.ExpiredCount = expired.Count;

        foreach (var log in expired)
        {
            session.Delete(log);
            summary.PurgedCount++;
        }

        _logger.LogDebug("Purged {Count} audit logs before {Cutoff}", summary.PurgedCount, cutoff);
    }

    private async Task PurgeAnalyticsAsync(
        IDocumentSession session,
        DateTimeOffset cutoff,
        RetentionCategorySummary summary,
        CancellationToken ct)
    {
        // FocusSessionRollupDocument purge
        var expiredRollups = await session.Query<FocusSessionRollupDocument>()
            .Where(x => x.SessionDate < cutoff.DateTime)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        foreach (var rollup in expiredRollups)
        {
            if (_options.UseSoftDelete)
                session.SoftDelete(rollup);
            else
                session.Delete(rollup);
            summary.PurgedCount++;
        }

        summary.ExpiredCount = expiredRollups.Count;
        _logger.LogDebug("Purged {Count} analytics documents before {Cutoff}", summary.PurgedCount, cutoff);
    }

    private async Task PurgeEngagementAsync(
        IDocumentSession session,
        DateTimeOffset cutoff,
        RetentionCategorySummary summary,
        CancellationToken ct)
    {
        // DailyChallengeCompletion purge
        var expiredChallenges = await session.Query<DailyChallengeCompletionDocument>()
            .Where(x => x.CompletedAt < cutoff)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        foreach (var doc in expiredChallenges)
        {
            session.Delete(doc);
            summary.PurgedCount++;
        }

        summary.ExpiredCount = expiredChallenges.Count;
        _logger.LogDebug("Purged {Count} engagement documents before {Cutoff}", summary.PurgedCount, cutoff);
    }

    private async Task ArchiveAndPurgeStudentRecordsAsync(
        IDocumentSession session,
        DateTimeOffset cutoff,
        RetentionCategorySummary summary,
        CancellationToken ct)
    {
        // For student records, we need to be more careful
        // First, identify inactive students (no activity since cutoff)
        var inactiveStudents = await session.Query<StudentActivityDocument>()
            .Where(x => x.LastActivityAt < cutoff)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        summary.ExpiredCount = inactiveStudents.Count;

        foreach (var student in inactiveStudents)
        {
            // Archive event stream before deletion (in real implementation)
            // For now, mark for soft-delete
            if (_options.UseSoftDelete)
                session.SoftDelete(student);

            summary.PurgedCount++;
        }

        _logger.LogDebug("Archived/purged {Count} inactive student records", summary.PurgedCount);
    }

    private async Task<int> AccelerateErasureRequestsAsync(IDocumentStore store, CancellationToken ct)
    {
        int accelerated = 0;

        try
        {
            await using var session = store.LightweightSession();

            // Find erasure requests in cooling period that are now past their retention
            var requests = await session.Query<ErasureRequestDocument>()
                .Where(x => x.Status == ErasureRequestStatus.CoolingPeriod)
                .ToListAsync(ct);

            var now = _clock.UtcNow;

            foreach (var request in requests)
            {
                // Accelerate if past cooling period
                if (request.RequestedAt.Add(request.CoolingPeriod) <= now)
                {
                    request.Status = ErasureRequestStatus.Approved; // Auto-approve after cooling
                    request.ApprovedAt = now;
                    session.Store(request);
                    accelerated++;

                    _logger.LogInformation(
                        "[SIEM] ErasureRequestAccelerated: {RequestId} for {StudentId}",
                        request.Id, request.StudentId);
                }
            }

            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accelerate erasure requests");
        }

        return accelerated;
    }
}

// Placeholder document classes for compilation - these would be in their actual locations
public sealed class FocusSessionRollupDocument
{
    public Guid Id { get; set; }
    public DateTime SessionDate { get; set; }
}

public sealed class DailyChallengeCompletionDocument
{
    public Guid Id { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class StudentActivityDocument
{
    public Guid Id { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
}

public sealed class ErasureRequestDocument
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ErasureRequestStatus Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public TimeSpan CoolingPeriod { get; set; } = TimeSpan.FromDays(14);
}

public enum ErasureRequestStatus { Pending, CoolingPeriod, Approved, Processing, Completed, Rejected }

/// <summary>
/// Extension methods for registering the retention worker.
/// </summary>
public static class RetentionWorkerExtensions
{
    public static IServiceCollection AddRetentionWorker(
        this IServiceCollection services,
        Action<RetentionWorkerOptions>? configure = null)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddHostedService<RetentionWorker>();
        services.AddSingleton<IRetentionPolicyService, DefaultRetentionPolicyService>();

        if (configure != null)
            services.Configure(configure);
        else
            services.Configure<RetentionWorkerOptions>(_ => { });

        return services;
    }
}

/// <summary>
/// Default implementation of retention policy service.
/// </summary>
public sealed class DefaultRetentionPolicyService : IRetentionPolicyService
{
    private readonly IDocumentStore _store;

    public DefaultRetentionPolicyService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<TimeSpan> GetRetentionPeriodAsync(string tenantId, DataCategory category, CancellationToken ct = default)
    {
        // Check for tenant override
        if (tenantId != "global")
        {
            await using var session = _store.QuerySession();
            var policy = await session.Query<TenantRetentionPolicy>()
                .Where(x => x.TenantId == tenantId && x.EffectiveFrom <= DateTimeOffset.UtcNow)
                .Where(x => x.EffectiveTo == null || x.EffectiveTo > DateTimeOffset.UtcNow)
                .FirstOrDefaultAsync(ct);

            if (policy != null)
            {
                var overrideValue = category switch
                {
                    DataCategory.StudentRecord => policy.StudentRecordRetentionOverride,
                    DataCategory.AuditLog => policy.AuditLogRetentionOverride,
                    DataCategory.Analytics => policy.AnalyticsRetentionOverride,
                    DataCategory.Engagement => policy.EngagementRetentionOverride,
                    _ => null
                };

                if (overrideValue.HasValue)
                    return overrideValue.Value;
            }
        }

        // Return default policy
        return category switch
        {
            DataCategory.StudentRecord => DataRetentionPolicy.StudentRecordRetention,
            DataCategory.AuditLog => DataRetentionPolicy.AuditLogRetention,
            DataCategory.Analytics => DataRetentionPolicy.AnalyticsRetention,
            DataCategory.Engagement => DataRetentionPolicy.EngagementRetention,
            _ => TimeSpan.FromDays(365)
        };
    }

    public async Task<TenantRetentionPolicy?> GetTenantPolicyAsync(string tenantId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<TenantRetentionPolicy>()
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.EffectiveFrom <= DateTimeOffset.UtcNow)
            .Where(x => x.EffectiveTo == null || x.EffectiveTo > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(ct);
    }
}
