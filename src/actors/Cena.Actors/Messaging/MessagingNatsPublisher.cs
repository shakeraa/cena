// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging NATS Publisher
// Layer: Infrastructure | Runtime: .NET 9
// Publishes messaging events to NATS JetStream.
// Does NOT throw on failure — logs error and continues.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cena.Actors.Events;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Messaging;

public sealed class MessagingNatsPublisher : IMessagingEventPublisher
{
    private readonly INatsConnection _nats;
    private readonly ILogger<MessagingNatsPublisher> _logger;

    private static readonly Meter Meter = new("Cena.Messaging.Nats", "1.0.0");
    private static readonly Counter<long> Published =
        Meter.CreateCounter<long>("cena.messaging.nats.published_total");
    private static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("cena.messaging.nats.failures_total");

    public MessagingNatsPublisher(INatsConnection nats, ILogger<MessagingNatsPublisher> logger)
    {
        _nats = nats;
        _logger = logger;
    }

    public Task PublishMessageSentAsync(MessageSent_V1 evt) =>
        PublishSafe(MessagingNatsSubjects.MessageSent, evt, evt.MessageId);

    public Task PublishMessageReadAsync(MessageRead_V1 evt) =>
        PublishSafe(MessagingNatsSubjects.MessageRead, evt, evt.MessageId);

    public Task PublishThreadCreatedAsync(ThreadCreated_V1 evt) =>
        PublishSafe(MessagingNatsSubjects.ThreadCreated, evt, evt.ThreadId);

    public Task PublishThreadMutedAsync(ThreadMuted_V1 evt) =>
        PublishSafe(MessagingNatsSubjects.ThreadMuted, evt, evt.ThreadId);

    public Task PublishMessageBlockedAsync(MessageBlocked_V1 evt) =>
        PublishSafe(MessagingNatsSubjects.MessageBlocked, evt, Guid.NewGuid().ToString("N"));

    public Task PublishInboundReceivedAsync(string source, string externalId, string text) =>
        PublishSafe(MessagingNatsSubjects.InboundReceived,
            new { Source = source, ExternalId = externalId,
                  TextSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant() },
            $"{source}:{externalId}");

    private async Task PublishSafe<T>(string subject, T payload, string deduplicationId)
    {
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(payload);

            var headers = new NatsHeaders
            {
                ["Nats-Msg-Id"] = deduplicationId,
                ["Cena-Correlation-Id"] = Guid.NewGuid().ToString("N"),
                ["Cena-Schema-Version"] = "1",
            };

            await _nats.PublishAsync(subject, data, headers: headers);

            Published.Add(1, new KeyValuePair<string, object?>("subject", subject));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "NATS publish failed: subject={Subject}, dedup={DeduplicationId}",
                subject, deduplicationId);

            Failures.Add(1, new KeyValuePair<string, object?>("subject", subject));
            // Do NOT rethrow — Redis is the hot store, NATS catches up on recovery
        }
    }
}
