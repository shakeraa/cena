// =============================================================================
// Cena Platform -- Embedding Ingestion Handler
// SAI-06: Subscribes to NATS content block extraction events and triggers
// async embedding via EmbeddingService. Decoupled from the ingestion pipeline
// to avoid blocking the main processing flow.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Services;

/// <summary>
/// Subscribes to NATS subject "cena.content.block.extracted" and embeds
/// content blocks asynchronously. Also processes any blocks that arrived
/// before this handler started (catch-up on startup).
/// </summary>
public sealed class EmbeddingIngestionHandler : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IDocumentStore _store;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<EmbeddingIngestionHandler> _logger;

    private const string NatsSubject = "cena.ingest.content.extracted";

    public EmbeddingIngestionHandler(
        INatsConnection nats,
        IDocumentStore store,
        IEmbeddingService embeddings,
        ILogger<EmbeddingIngestionHandler> logger)
    {
        _nats = nats;
        _store = store;
        _embeddings = embeddings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give time for pgvector migration to complete
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        _logger.LogInformation("SAI-06: Embedding ingestion handler started, subscribing to {Subject}", NatsSubject);

        try
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(NatsSubject, cancellationToken: stoppingToken))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<ContentBlockExtractedPayload>(msg.Data ?? []);
                    if (payload?.ContentBlockId is null)
                        continue;

                    await EmbedBlockByIdAsync(payload.ContentBlockId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to process embedding for NATS message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SAI-06: Embedding ingestion handler stopping");
        }
    }

    private async Task EmbedBlockByIdAsync(string contentBlockId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var block = await session.LoadAsync<ContentBlockDocument>(contentBlockId, ct);

        if (block is null)
        {
            _logger.LogDebug("Content block {BlockId} not found for embedding", contentBlockId);
            return;
        }

        await _embeddings.EmbedContentBlockAsync(block, ct);
    }

    private sealed record ContentBlockExtractedPayload(string? ContentBlockId);
}
