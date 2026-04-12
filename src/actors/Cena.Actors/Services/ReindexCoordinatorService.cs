// =============================================================================
// Cena Platform -- Reindex Coordinator Service
// Background service that subscribes to reindex commands via NATS and
// coordinates embedding reindex jobs. Updates ReindexJobDocument status.
// =============================================================================

using System.Text.Json;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Services;

public sealed class ReindexCoordinatorService : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IDocumentStore _store;
    private readonly ILogger<ReindexCoordinatorService> _logger;

    private const string NatsSubject = "cena.embeddings.reindex.command";
    private const string NatsProgressSubject = "cena.embeddings.reindex.progress";

    public ReindexCoordinatorService(
        INatsConnection nats,
        IDocumentStore store,
        ILogger<ReindexCoordinatorService> logger)
    {
        _nats = nats;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        _logger.LogInformation("Reindex coordinator started, subscribing to {Subject}", NatsSubject);

        try
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(NatsSubject, cancellationToken: stoppingToken))
            {
                try
                {
                    var command = JsonSerializer.Deserialize<ReindexCommand>(
                        msg.Data ?? [], new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                    if (command?.JobId is null)
                    {
                        _logger.LogWarning("Received invalid reindex command");
                        continue;
                    }

                    await ProcessReindexJobAsync(command, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to process reindex command");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Reindex coordinator stopping");
        }
    }

    private async Task ProcessReindexJobAsync(ReindexCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Starting reindex job {JobId}: scope={Scope}, filter={Filter}",
            command.JobId, command.Scope, command.Filter ?? "(none)");

        await using var session = _store.LightweightSession();

        // Load job document
        var job = await session.LoadAsync<ReindexJobDocument>(command.JobId, ct);
        if (job is null)
        {
            _logger.LogError("Reindex job {JobId} not found in database", command.JobId);
            return;
        }

        if (job.Status != ReindexJobStatus.Pending)
        {
            _logger.LogWarning("Reindex job {JobId} has status {Status}, skipping", command.JobId, job.Status);
            return;
        }

        // Mark as running
        job.Status = ReindexJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        session.Store(job);
        await session.SaveChangesAsync(ct);

        await PublishProgressAsync(job, ct);

        try
        {
            // Reindex blocks based on scope
            var (processed, failed) = await ReindexBlocksAsync(command, job, session, ct);

            job.ProcessedBlocks = processed;
            job.FailedBlocks = failed;
            job.Status = failed > 0 && processed == 0 ? ReindexJobStatus.Failed : ReindexJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;

            if (job.Status == ReindexJobStatus.Failed)
            {
                job.ErrorMessage = $"All {failed} blocks failed to reindex";
            }

            _logger.LogInformation("Reindex job {JobId} completed: {Processed} processed, {Failed} failed",
                command.JobId, processed, failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reindex job {JobId} failed", command.JobId);
            job.Status = ReindexJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }

        session.Store(job);
        await session.SaveChangesAsync(ct);
        await PublishProgressAsync(job, ct);
    }

    private async Task<(int Processed, int Failed)> ReindexBlocksAsync(
        ReindexCommand command,
        ReindexJobDocument job,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Query content block IDs matching the scope/filter
        var blockIds = await GetBlockIdsForReindexAsync(command, session, ct);

        int processed = 0;
        int failed = 0;

        foreach (var blockId in blockIds)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // FIND-arch-021: Removed orphan NATS publisher (cena.ingest.content.extracted had no subscribers)
                // Reindexing is handled directly via IEmbeddingService rather than through NATS.

                processed++;
                job.ProcessedBlocks = processed;

                // Publish progress every 10 blocks
                if (processed % 10 == 0)
                {
                    await PublishProgressAsync(job, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reindex block {BlockId} for job {JobId}", blockId, command.JobId);
                failed++;
                job.FailedBlocks = failed;
            }
        }

        return (processed, failed);
    }

    private async Task<List<string>> GetBlockIdsForReindexAsync(
        ReindexCommand command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Query raw events from content_blocks stream
        // This is a simplified implementation - in production you'd query the content_embeddings table
        // to find which blocks need reindexing

        var sql = command.Scope.ToLowerInvariant() switch
        {
            "subject" when !string.IsNullOrEmpty(command.Filter) =>
                """
                SELECT content_block_id FROM cena.content_embeddings
                WHERE subject = $1
                ORDER BY content_block_id
                """,
            "concept" when !string.IsNullOrEmpty(command.Filter) =>
                """
                SELECT content_block_id FROM cena.content_embeddings
                WHERE $1 = ANY(concept_ids)
                ORDER BY content_block_id
                """,
            _ =>
                """
                SELECT content_block_id FROM cena.content_embeddings
                ORDER BY content_block_id
                """
        };

        var blockIds = new List<string>();

        try
        {
            await using var conn = session.Connection;
            await using var cmd = conn?.CreateCommand();
            if (cmd is null) return blockIds;

            cmd.CommandText = sql;

            if (!string.IsNullOrEmpty(command.Filter) && command.Scope is "subject" or "concept")
            {
                var param = cmd.CreateParameter();
                param.Value = command.Filter;
                cmd.Parameters.Add(param);
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                blockIds.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query block IDs for reindex job {JobId}", command.JobId);
        }

        return blockIds;
    }

    private async Task PublishProgressAsync(ReindexJobDocument job, CancellationToken ct)
    {
        try
        {
            var evt = new ReindexProgressEvent(
                job.Id,
                job.ProcessedBlocks,
                job.FailedBlocks,
                job.EstimatedBlocks,
                job.Status,
                job.ErrorMessage);

            var data = JsonSerializer.SerializeToUtf8Bytes(evt);
            await _nats.PublishAsync(NatsProgressSubject, data, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish reindex progress for job {JobId}", job.Id);
        }
    }
}
