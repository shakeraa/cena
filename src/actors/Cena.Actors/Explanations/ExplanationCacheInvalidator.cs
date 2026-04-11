// =============================================================================
// Cena Platform -- Explanation Cache Invalidator (NATS Subscriber)
// SAI-003.5: Invalidates L2 Redis cache when question content changes
//
// Subscribes to the durable JetStream subjects emitted by NatsOutboxPublisher
// when QuestionStemEdited_V1 / QuestionOptionChanged_V1 events are committed
// to Marten. The outbox uses the convention
//   cena.durable.{category}.{EventTypeName}
// and curriculum events land on
//   cena.durable.curriculum.QuestionStemEdited_V1
//   cena.durable.curriculum.QuestionOptionChanged_V1
// These subjects are produced by NatsSubjects.DurableCurriculumEvent(...) and
// by NatsOutboxPublisher.GetDurableSubject(...), both of which share the same
// prefix helper so a drift here is now a compile-time dependency mismatch.
// On receipt, calls IExplanationCacheService.InvalidateQuestionAsync to
// delete all ErrorType variants for the affected question.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Events;
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
        // NatsOutboxPublisher publishes curriculum events (Question*, Pipeline*, File*)
        // on cena.durable.curriculum.{EventTypeName}. We compute the exact subjects
        // here via the typed registry (NatsSubjects.DurableCurriculumEvent) so the
        // subscribe side cannot drift from the publish side.
        var stemSubject = NatsSubjects.DurableCurriculumEvent(nameof(QuestionStemEdited_V1));
        var optionSubject = NatsSubjects.DurableCurriculumEvent(nameof(QuestionOptionChanged_V1));

        _logger.LogInformation(
            "SAI-003: Explanation cache invalidator starting NATS subscriptions on {Stem} and {Option}",
            stemSubject, optionSubject);

        var stemTask = SubscribeAsync(stemSubject, stoppingToken);
        var optionTask = SubscribeAsync(optionSubject, stoppingToken);

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
