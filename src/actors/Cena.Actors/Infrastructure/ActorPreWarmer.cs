// =============================================================================
// Cena Platform -- Actor Pre-Warmer (RES-010)
// Activates student actors in controlled batches before peak load windows.
//
// Usage:
//   1. Admin API publishes student IDs to NatsSubjects.WarmUpRequest
//   2. This service picks them up and sends WarmUp messages to each actor
//   3. Actors cold-start, restore state from Marten, and sit warm in memory
//
// Design: batched activation with configurable concurrency to avoid
// thundering herd on the PG connection pool during pre-warm.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Students;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Proto;
using Proto.Cluster;

namespace Cena.Actors.Infrastructure;

public sealed class ActorPreWarmer : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly ActorSystem _actorSystem;
    private readonly ILogger<ActorPreWarmer> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const int WarmUpBatchSize = 10;
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InterBatchDelay = TimeSpan.FromMilliseconds(500);

    public ActorPreWarmer(
        INatsConnection nats,
        ActorSystem actorSystem,
        ILogger<ActorPreWarmer> logger)
    {
        _nats = nats;
        _actorSystem = actorSystem;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for cluster readiness
        while (_actorSystem.Cluster()?.MemberList?.GetMembers()?.Count is null or 0)
        {
            if (stoppingToken.IsCancellationRequested) return;
            await Task.Delay(500, stoppingToken);
        }

        _logger.LogInformation("ActorPreWarmer ready — listening on {Subject}", NatsSubjects.WarmUpRequest);

        try
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(NatsSubjects.WarmUpRequest, cancellationToken: stoppingToken))
            {
                try
                {
                    var rawData = msg.Data;
                    if (rawData is null || rawData.Length == 0) continue;

                    var request = JsonSerializer.Deserialize<WarmUpRequest>(rawData, _jsonOpts);
                    if (request?.StudentIds is { Count: > 0 })
                    {
                        await WarmUpBatchAsync(request.StudentIds, stoppingToken);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize warm-up request");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WarmUpBatchAsync(IReadOnlyList<string> studentIds, CancellationToken ct)
    {
        _logger.LogInformation("Pre-warming {Count} actors in batches of {BatchSize}...",
            studentIds.Count, WarmUpBatchSize);

        var warmed = 0;
        var failed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var batch in studentIds.Chunk(WarmUpBatchSize))
        {
            if (ct.IsCancellationRequested) break;

            var tasks = batch.Select(async studentId =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(WarmUpTimeout);
                    var result = await _actorSystem.Cluster()
                        .RequestAsync<ActorResult>(studentId, "student", new WarmUp(), cts.Token);
                    return result?.Success == true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Warm-up failed for student {StudentId}", studentId);
                    return false;
                }
            });

            var results = await Task.WhenAll(tasks);
            warmed += results.Count(r => r);
            failed += results.Count(r => !r);

            // Brief pause between batches to avoid PG pool pressure
            await Task.Delay(InterBatchDelay, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        sw.Stop();
        _logger.LogInformation(
            "Pre-warm complete: {Warmed}/{Total} actors activated in {ElapsedMs}ms ({Failed} failed)",
            warmed, studentIds.Count, sw.ElapsedMilliseconds, failed);
    }
}

/// <summary>
/// Request payload for actor pre-warming.
/// Published to NatsSubjects.WarmUpRequest by Admin API or scheduler.
/// </summary>
public sealed record WarmUpRequest(IReadOnlyList<string> StudentIds);
