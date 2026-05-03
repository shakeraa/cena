// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging Event Publisher Interface
// Layer: Domain Interface | Runtime: .NET 9
// Abstracts NATS JetStream publishing for the Messaging context.
// ═══════════════════════════════════════════════════════════════════════

using Cena.Actors.Events;

namespace Cena.Actors.Messaging;

/// <summary>
/// Publishes messaging domain events to NATS JetStream for audit trail
/// and cross-context integration. Implementations should NOT throw on
/// failure — Redis is the hot store, NATS catches up on recovery.
/// </summary>
public interface IMessagingEventPublisher
{
    Task PublishMessageSentAsync(MessageSent_V1 evt);
    Task PublishMessageReadAsync(MessageRead_V1 evt);
    Task PublishThreadCreatedAsync(ThreadCreated_V1 evt);
    Task PublishThreadMutedAsync(ThreadMuted_V1 evt);
    Task PublishMessageBlockedAsync(MessageBlocked_V1 evt);
    Task PublishInboundReceivedAsync(string source, string externalId, string text);
}

/// <summary>
/// NATS subject constants for the Messaging bounded context.
/// </summary>
public static class MessagingNatsSubjects
{
    public const string MessageSent = "cena.messaging.events.MessageSent";
    public const string MessageRead = "cena.messaging.events.MessageRead";
    public const string ThreadCreated = "cena.messaging.events.ThreadCreated";
    public const string ThreadMuted = "cena.messaging.events.ThreadMuted";
    public const string MessageBlocked = "cena.messaging.events.MessageBlocked";
    public const string InboundReceived = "cena.messaging.events.InboundReceived";
    public const string MessagesArchived = "cena.messaging.events.MessagesArchived";

    public const string CmdSendMessage = "cena.messaging.commands.SendMessage";
    public const string CmdRouteInboundReply = "cena.messaging.commands.RouteInboundReply";
    public const string CmdBroadcastToClass = "cena.messaging.commands.BroadcastToClass";
}
