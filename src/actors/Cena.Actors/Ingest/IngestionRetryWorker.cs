// =============================================================================
// Cena Platform — Ingestion retry background worker (PRR-RETRY-IMPL).
// Scans on a fixed cadence for stuck-Incoming pipeline items the curator
// asked to retry (RetryCount > 0, past backoff window), reconstructs the
// IngestionRequest from the persisted PipelineItemDocument + the bytes
// store, and re-invokes IIngestionOrchestrator.ProcessFileAsync. Each
// retried item is held by a Postgres advisory lock so two parallel
// workers (and any concurrent admin click) cannot replay the same item
// twice.
//
// Backoff: exponential capped at 16 minutes (1 → 2 → 4 → 8 → 16). Items
// past MaxAttempts are flipped to Status="failed" with a clear LastError
// so curators see "max retries exceeded" rather than perpetual replays.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Ingest;

public sealed class IngestionRetryWorkerOptions
{
    /// <summary>Tick interval. 60s default keeps Postgres load negligible.</summary>
    public int TickSeconds { get; set; } = 60;

    /// <summary>Hard cap on retries before the item is flipped to failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Master switch — set false in tests / dev that don't want a worker.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class IngestionRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionRetryWorkerOptions _options;
    private readonly ILogger<IngestionRetryWorker> _logger;

    public IngestionRetryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionRetryWorkerOptions> options,
        ILogger<IngestionRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("IngestionRetryWorker disabled by configuration; exiting.");
            return;
        }

        _logger.LogInformation(
            "IngestionRetryWorker started: tick={Tick}s, maxAttempts={Max}",
            _options.TickSeconds, _options.MaxAttempts);

        var tick = TimeSpan.FromSeconds(Math.Max(5, _options.TickSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IngestionRetryWorker tick failed; will retry next interval.");
            }

            try { await Task.Delay(tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Public for testability — lets a unit test drive a single tick deterministically
    /// without standing up a hosted-service runtime.
    /// </summary>
    public async Task TickOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var store = sp.GetRequiredService<IDocumentStore>();
        var bytesStore = sp.GetRequiredService<IIngestionBytesStore>();
        var orchestrator = sp.GetRequiredService<IIngestionOrchestrator>();

        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<PipelineItemDocument> candidates;
        await using (var session = store.QuerySession())
        {
            // Query selectivity: Status="processing" AND CurrentStage=Incoming
            // AND RetryCount>0 — narrow set in practice. The backoff filter
            // is applied in-memory after the LINQ query because Backoff
            // depends on RetryCount in a way Marten's LINQ provider can't
            // translate to SQL cleanly.
            candidates = await session.Query<PipelineItemDocument>()
                .Where(x => x.Status == "processing"
                            && x.CurrentStage == PipelineStage.Incoming
                            && x.RetryCount > 0)
                .Take(50)
                .ToListAsync(ct);
        }

        foreach (var item in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var since = now - item.UpdatedAt;
            if (since < BackoffFor(item.RetryCount)) continue;

            await ProcessOneAsync(item, store, bytesStore, orchestrator, ct);
        }
    }

    private async Task ProcessOneAsync(
        PipelineItemDocument item,
        IDocumentStore store,
        IIngestionBytesStore bytesStore,
        IIngestionOrchestrator orchestrator,
        CancellationToken ct)
    {
        // Per-item advisory lock prevents two replay attempts (e.g. two
        // worker instances behind a load balancer, or worker + admin
        // click) from racing on the same item. Lock key derived from id;
        // unrelated items never contend. The lock is held on a dedicated
        // connection because the orchestrator opens its own Marten
        // sessions internally — sharing the lock connection with Marten
        // would deadlock when the orchestrator tries to commit.
        var lockKey = AdvisoryLockKey(item.Id);
        await using var lockConn = (Npgsql.NpgsqlConnection)store.Storage.Database.CreateConnection();
        await lockConn.OpenAsync(ct);

        bool acquired;
        await using (var tryLock = lockConn.CreateCommand())
        {
            tryLock.CommandText = "SELECT pg_try_advisory_lock(@k);";
            var p = tryLock.CreateParameter();
            p.ParameterName = "k";
            p.Value = lockKey;
            tryLock.Parameters.Add(p);
            acquired = (bool)(await tryLock.ExecuteScalarAsync(ct) ?? false);
        }
        if (!acquired)
        {
            _logger.LogDebug("Skipping {ItemId}: another worker holds the retry lock.", item.Id);
            return;
        }

        try
        {
            // Refresh the doc under the lock — it may have moved on (curator
            // rejected, or a previous tick already retried) since the scan.
            PipelineItemDocument? fresh;
            await using (var s = store.QuerySession())
                fresh = await s.LoadAsync<PipelineItemDocument>(item.Id, ct);
            if (fresh is null
                || fresh.Status != "processing"
                || fresh.CurrentStage != PipelineStage.Incoming
                || fresh.RetryCount == 0)
            {
                return;
            }

            // Max-attempts gate: flip to failed and stop trying.
            if (fresh.RetryCount > _options.MaxAttempts)
            {
                await MarkFailedAsync(store, fresh,
                    $"max retries exceeded ({_options.MaxAttempts})", ct);
                return;
            }

            // Bytes-not-persisted (legacy items): refuse cleanly. Curator
            // gets a "please re-upload" error from the SPA via LastError.
            if (!fresh.BytesPersisted)
            {
                await MarkFailedAsync(store, fresh,
                    "original bytes were not persisted at upload — re-upload required", ct);
                return;
            }

            byte[]? bytes;
            try
            {
                bytes = await bytesStore.GetAsync(fresh.S3Key, ct);
            }
            catch (Exception ex)
            {
                // Transient store error — leave for the next tick. Don't
                // burn a retry attempt for a network blip.
                _logger.LogWarning(ex,
                    "Bytes-store GET transient failure for {ItemId}; will retry next tick.",
                    fresh.Id);
                return;
            }
            if (bytes is null)
            {
                await MarkFailedAsync(store, fresh,
                    $"persisted bytes missing at {fresh.S3Key} — re-upload required", ct);
                return;
            }

            _logger.LogInformation(
                "Retrying pipeline item {ItemId} (attempt {Attempt}/{Max}, {Bytes} bytes)",
                fresh.Id, fresh.RetryCount, _options.MaxAttempts, bytes.Length);

            using var stream = new MemoryStream(bytes, writable: false);
            var result = await orchestrator.ProcessFileAsync(new IngestionRequest(
                FileStream: stream,
                Filename: fresh.SourceFilename,
                ContentType: fresh.ContentType,
                SourceType: fresh.SourceType,
                SourceUrl: fresh.SourceUrl,
                SubmittedBy: fresh.SubmittedBy), ct);

            // ProcessFileAsync creates a NEW PipelineItemDocument with a
            // fresh "pi-" id (content-addressed via Guid). The original
            // doc the curator clicked Retry on is what we still hold.
            // Mark the old doc terminal-success or terminal-failure so
            // the kanban shows progress; the curator follows the new
            // pipelineItemId via the result.
            await using var session = store.LightweightSession();
            var docToUpdate = await session.LoadAsync<PipelineItemDocument>(fresh.Id, ct);
            if (docToUpdate is null) return;

            if (result.Success)
            {
                docToUpdate.Status = "completed";
                docToUpdate.CurrentStage = PipelineStage.Classified;
                docToUpdate.LastError = null;
                docToUpdate.CompletedAt = DateTimeOffset.UtcNow;
                docToUpdate.UpdatedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Retry succeeded for {OldItemId}; new pipeline doc = {NewItemId}",
                    fresh.Id, result.PipelineItemId);
            }
            else
            {
                // Increment-on-failure: the next tick will gate by Backoff(RetryCount)
                // before trying again, and the MaxAttempts cap above takes effect.
                docToUpdate.RetryCount++;
                docToUpdate.LastError = result.ErrorMessage;
                docToUpdate.UpdatedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "Retry failed for {ItemId}: {Error}", fresh.Id, result.ErrorMessage);
            }
            session.Store(docToUpdate);
            await session.SaveChangesAsync(ct);
        }
        finally
        {
            // Best-effort unlock. Connection close also drops session locks,
            // so an exception here doesn't strand the lock past lockConn
            // dispose.
            try
            {
                await using var unlock = lockConn.CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(@k);";
                var p = unlock.CreateParameter();
                p.ParameterName = "k";
                p.Value = lockKey;
                unlock.Parameters.Add(p);
                await unlock.ExecuteNonQueryAsync(ct);
            }
            catch { /* connection dispose will release */ }
        }
    }

    private static async Task MarkFailedAsync(
        IDocumentStore store, PipelineItemDocument item, string reason, CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        var doc = await session.LoadAsync<PipelineItemDocument>(item.Id, ct);
        if (doc is null) return;
        doc.Status = "failed";
        doc.CurrentStage = PipelineStage.Failed;
        doc.LastError = reason;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Exponential capped at 16 min: 1, 2, 4, 8, 16, 16, … . Public so tests
    /// in any assembly can pin the schedule and so curator-facing diagnostics
    /// can preview "next retry at" without re-deriving the formula.
    /// </summary>
    public static TimeSpan BackoffFor(int retryCount) =>
        TimeSpan.FromMinutes(retryCount <= 0 ? 0 : Math.Min(16, Math.Pow(2, retryCount - 1)));

    private static long AdvisoryLockKey(string itemId)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(itemId);
        var sha = System.Security.Cryptography.SHA256.HashData(bytes);
        return BitConverter.ToInt64(sha, 0);
    }
}
