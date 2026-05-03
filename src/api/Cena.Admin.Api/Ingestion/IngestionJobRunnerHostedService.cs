// =============================================================================
// Cena Platform — IngestionJobRunnerHostedService
//
// Channel-driven worker that executes queued IngestionJobDocument rows.
// One job at a time per process (sufficient for dev; bound queue
// concurrency via WorkerCount field if scaling). On host start:
//
//   1. Rehydrate Marten — any Queued/Running rows from a previous boot
//      are flipped back to Queued and re-injected into the channel.
//   2. Loop: read next id, load doc, dispatch to strategy, mark terminal.
//
// Strategies are pluggable (`IIngestionJobStrategy` keyed by job type).
// Cooperative cancellation: each tick checks `CancelRequested` from the
// doc; the linked CancellationToken is honoured at strategy checkpoints.
// =============================================================================

using System.Text.Json;
using System.Threading.Channels;
using Cena.Admin.Api.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Ingestion;

public interface IIngestionJobStrategy
{
    IngestionJobType Type { get; }

    Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct);
}

public interface IJobProgressReporter
{
    Task ReportAsync(int pct, string? message, CancellationToken ct = default);
    Task LogAsync(string level, string message, CancellationToken ct = default);
    bool CancelRequested { get; }
}

internal sealed class IngestionJobRunnerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _channel;
    private readonly ILogger<IngestionJobRunnerHostedService> _logger;

    // Cancellation cadence: how often we re-read the doc to honour
    // CancelRequested while a strategy is mid-flight.
    private static readonly TimeSpan CancelPollInterval = TimeSpan.FromSeconds(2);

    public IngestionJobRunnerHostedService(
        IServiceScopeFactory scopeFactory,
        Channel<string> channel,
        ILogger<IngestionJobRunnerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionJobRunner started");

        // Stagger boot: let the rest of the host finish coming up before
        // we hammer Marten with a rehydrate query.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Pull leftover Queued/Running rows back into the channel.
        await using (var bootScope = _scopeFactory.CreateAsyncScope())
        {
            try
            {
                var jobs = bootScope.ServiceProvider.GetRequiredService<IIngestionJobService>();
                var pending = await jobs.RehydrateAsync(stoppingToken);
                foreach (var id in pending)
                {
                    if (!_channel.Writer.TryWrite(id))
                    {
                        _logger.LogWarning("Rehydrate channel full; lost {JobId}", id);
                    }
                }
                if (pending.Count > 0)
                {
                    _logger.LogInformation("Rehydrated {Count} pending ingestion jobs", pending.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion job rehydrate failed");
            }
        }

        await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ExecuteJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion job {JobId} runner crashed", jobId);
            }
        }

        _logger.LogInformation("IngestionJobRunner stopped");
    }

    private async Task ExecuteJobAsync(string jobId, CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var jobs = sp.GetRequiredService<IIngestionJobService>();
        var strategies = sp.GetServices<IIngestionJobStrategy>().ToList();

        var doc = await jobs.GetAsync(jobId, stoppingToken);
        if (doc is null)
        {
            _logger.LogWarning("Ingestion job {JobId} not found in Marten; skipped", jobId);
            return;
        }

        // Pre-start cancel check (cancel-while-queued).
        if (doc.CancelRequested
            || doc.Status is IngestionJobStatus.Cancelled
                            or IngestionJobStatus.Completed
                            or IngestionJobStatus.Failed)
        {
            return;
        }

        var strategy = strategies.FirstOrDefault(s => s.Type == doc.Type);
        if (strategy is null)
        {
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Failed,
                $"No strategy registered for job type '{doc.Type}'", null, stoppingToken);
            return;
        }

        await jobs.MarkStartedAsync(jobId, stoppingToken);

        // Linked cancellation: stoppingToken (host shutdown) + a watcher
        // that flips when CancelRequested goes true.
        using var cancelCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var cancelWatcher = WatchCancelAsync(jobId, cancelCts, stoppingToken);

        var reporter = new ProgressReporter(jobId, jobs);
        try
        {
            var result = await strategy.ExecuteAsync(doc, sp, reporter, cancelCts.Token);
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Completed, null, result, stoppingToken);
            _logger.LogInformation("Ingestion job {JobId} completed", jobId);
        }
        catch (OperationCanceledException) when (cancelCts.IsCancellationRequested
                                                  && !stoppingToken.IsCancellationRequested)
        {
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Cancelled,
                "Cancelled by user", null, stoppingToken);
            _logger.LogInformation("Ingestion job {JobId} cancelled", jobId);
        }
        catch (Exception ex)
        {
            await jobs.MarkTerminalAsync(jobId, IngestionJobStatus.Failed,
                ex.Message, null, stoppingToken);
            _logger.LogError(ex, "Ingestion job {JobId} failed", jobId);
        }
        finally
        {
            try { cancelCts.Cancel(); } catch { /* idempotent cancel */ }
            try { await cancelWatcher; } catch { /* watcher exit is best-effort */ }
        }
    }

    private async Task WatchCancelAsync(
        string jobId, CancellationTokenSource cts, CancellationToken stop)
    {
        while (!cts.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            try { await Task.Delay(CancelPollInterval, cts.Token); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IIngestionJobService>();
                var doc = await jobs.GetAsync(jobId, stop);
                if (doc?.CancelRequested == true)
                {
                    cts.Cancel();
                    return;
                }
            }
            catch { /* transient — try again next tick */ }
        }
    }

    private sealed class ProgressReporter : IJobProgressReporter
    {
        private readonly string _jobId;
        private readonly IIngestionJobService _jobs;

        public ProgressReporter(string jobId, IIngestionJobService jobs)
        {
            _jobId = jobId;
            _jobs = jobs;
        }

        public bool CancelRequested
        {
            get
            {
                var doc = _jobs.GetAsync(_jobId).GetAwaiter().GetResult();
                return doc?.CancelRequested == true;
            }
        }

        public async Task ReportAsync(int pct, string? message, CancellationToken ct = default)
        {
            await _jobs.UpdateProgressAsync(_jobId, pct, message, ct);
            // Also log the progress message so the per-job log stream
            // forms a complete narrative (otherwise the SPA only sees
            // the latest progressMessage, not the history).
            if (!string.IsNullOrWhiteSpace(message))
                await _jobs.AppendLogAsync(_jobId, "info", $"[{pct}%] {message}", ct);
        }

        public Task LogAsync(string level, string message, CancellationToken ct = default) =>
            _jobs.AppendLogAsync(_jobId, level, message, ct);
    }
}

