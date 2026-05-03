// =============================================================================
// Cena Platform — CloudDirectoryWatcherHostedService
//
// Drives the "Auto-Watch" toggle on each saved cloud directory in the admin
// Ingestion Settings page. Every TickInterval, loads
// IngestionSettingsDocument, and for each entry that is
//   - Enabled = true
//   - AutoWatch = true
//   - last-run timestamp older than WatchIntervalMinutes (defaulting to 5)
// dispatches IIngestionPipelineService.IngestCloudDirectoryAsync with empty
// FileKeys (i.e. "ingest everything new"). The provider's SHA-256 dedup
// gate keeps already-ingested files from being re-queued, so an idempotent
// re-scan is cheap.
//
// State (last-run timestamps + per-dir mutex) is in-memory by design:
// crashes/restarts simply re-scan, which the dedup gate makes safe.
// =============================================================================

using System.Collections.Concurrent;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion;

internal sealed class CloudDirectoryWatcherHostedService : BackgroundService
{
    // Tick cadence: how often we look at the settings doc. Per-dir
    // intervals (WatchIntervalMinutes) are honoured on top of this — a
    // dir with WatchIntervalMinutes=15 simply gets visited but skipped
    // by the elapsed-check until 15 minutes have passed.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

    // Conservative default if a saved entry has no WatchIntervalMinutes.
    private const int DefaultIntervalMinutes = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudDirectoryWatcherHostedService> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRunByDirId = new();
    private readonly ConcurrentDictionary<string, byte> _inFlightDirIds = new();

    public CloudDirectoryWatcherHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CloudDirectoryWatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CloudDirectoryWatcherHostedService started; tick={TickInterval}s",
            (int)TickInterval.TotalSeconds);

        // Stagger start so we don't hit Marten on the same beat as
        // Bagrut's startup-check probes.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloud-dir watcher tick failed");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("CloudDirectoryWatcherHostedService stopped");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetService<IIngestionSettingsService>();
        var pipelineService = scope.ServiceProvider.GetService<IIngestionPipelineService>();
        if (settingsService is null || pipelineService is null) return;

        var settings = await settingsService.GetSettingsAsync();
        if (settings.CloudDirectories.Count == 0) return;

        var now = DateTimeOffset.UtcNow;

        foreach (var dir in settings.CloudDirectories)
        {
            ct.ThrowIfCancellationRequested();
            if (!dir.Enabled || !dir.AutoWatch) continue;
            if (string.IsNullOrWhiteSpace(dir.Path)) continue;
            if (string.IsNullOrWhiteSpace(dir.Provider)) continue;

            var intervalMinutes = dir.WatchIntervalMinutes is int m && m > 0
                ? m
                : DefaultIntervalMinutes;

            if (_lastRunByDirId.TryGetValue(dir.Id, out var lastRun)
                && now - lastRun < TimeSpan.FromMinutes(intervalMinutes))
            {
                continue;
            }

            // Refuse re-entrancy per dir: if a previous tick is still
            // ingesting this directory (slow OCR, long file list), let
            // it finish before scheduling another pass.
            if (!_inFlightDirIds.TryAdd(dir.Id, 1)) continue;

            try
            {
                var request = new IngestionDto.CloudDirIngestRequest(
                    Provider: dir.Provider,
                    BucketOrPath: dir.Path,
                    FileKeys: Array.Empty<string>(),
                    Prefix: string.IsNullOrWhiteSpace(dir.Prefix) ? null : dir.Prefix);

                var response = await pipelineService.IngestCloudDirectoryAsync(request);

                _lastRunByDirId[dir.Id] = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Auto-watch scan {Provider}:{Path} (id={DirId}) → queued={Queued} skipped={Skipped} batch={BatchId}",
                    dir.Provider, dir.Path, dir.Id,
                    response.FilesQueued, response.FilesSkipped, response.BatchId);
            }
            catch (UnauthorizedAccessException ex)
            {
                _lastRunByDirId[dir.Id] = DateTimeOffset.UtcNow;
                _logger.LogWarning(ex,
                    "Auto-watch dispatch rejected for {Path} (id={DirId}); honouring interval to avoid log spam",
                    dir.Path, dir.Id);
            }
            catch (Exception ex)
            {
                // Don't update lastRun — we'll retry on the next tick
                // until WatchIntervalMinutes guards us against tight loops.
                _logger.LogError(ex,
                    "Auto-watch dispatch failed for {Path} (id={DirId})",
                    dir.Path, dir.Id);
            }
            finally
            {
                _inFlightDirIds.TryRemove(dir.Id, out _);
            }
        }
    }
}
