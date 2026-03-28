// =============================================================================
// Cena Platform -- Explanation Cache Invalidator (NATS Subscriber)
// SAI-003.5: Invalidates L2 Redis cache when question content changes
//
// Subscribes to: cena.question.stem.edited, cena.question.option.changed
// On receipt, calls IExplanationCacheService.InvalidateQuestionAsync to
// delete all ErrorType variants for the affected question.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Explanations;

/// <summary>
/// Background service that subscribes to NATS question-edit events and
/// invalidates the L2 explanation cache for modified questions.
/// </summary>
public sealed class ExplanationCacheInvalidator : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IExplanationCacheService _cache;
    private readonly ILogger<ExplanationCacheInvalidator> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExplanationCacheInvalidator(
        INatsConnection nats,
        IExplanationCacheService cache,
        ILogger<ExplanationCacheInvalidator> logger)
    {
        _nats = nats;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SAI-003: Explanation cache invalidator starting NATS subscriptions");

        // Outbox publishes events as: cena.events.{EventTypeName}
        var stemTask = SubscribeAsync("cena.events.QuestionStemEdited_V1", stoppingToken);
        var optionTask = SubscribeAsync("cena.events.QuestionOptionChanged_V1", stoppingToken);

        await Task.WhenAll(stemTask, optionTask);
    }

    private async Task SubscribeAsync(string subject, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(subject, cancellationToken: ct))
            {
                try
                {
                    var questionId = ExtractQuestionId(msg.Data);
                    if (string.IsNullOrWhiteSpace(questionId))
                    {
                        _logger.LogWarning(
                            "SAI-003: Received {Subject} event with no questionId", subject);
                        continue;
                    }

                    await _cache.InvalidateQuestionAsync(questionId, ct);

                    _logger.LogInformation(
                        "SAI-003: Invalidated explanation cache for question {QuestionId} " +
                        "due to {Subject} event",
                        questionId, subject);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "SAI-003: Failed to process {Subject} cache invalidation event", subject);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAI-003: NATS subscription for {Subject} terminated unexpectedly", subject);
        }
    }

    /// <summary>
    /// Extracts the questionId from the NATS message payload.
    /// Supports JSON payloads with a "questionId" field, or plain UTF-8 string IDs.
    /// </summary>
    private static string? ExtractQuestionId(byte[]? data)
    {
        if (data == null || data.Length == 0)
            return null;

        try
        {
            // Try JSON first (standard event shape)
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("questionId", out var prop))
                return prop.GetString();

            // Fallback: camelCase variants
            if (doc.RootElement.TryGetProperty("QuestionId", out var prop2))
                return prop2.GetString();
        }
        catch (JsonException)
        {
            // Not JSON — try as plain string
        }

        // Fallback: treat entire payload as a plain question ID string
        var text = System.Text.Encoding.UTF8.GetString(data).Trim();
        return text.Length > 0 && text.Length <= 128 ? text : null;
    }
}
