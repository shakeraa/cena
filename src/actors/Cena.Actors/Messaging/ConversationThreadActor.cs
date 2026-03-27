// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — ConversationThreadActor (Virtual, Redis-Backed)
// Layer: Actor | Runtime: .NET 9 | Framework: Proto.Actor v1.x
// Manages bidirectional human messaging. State lives in Redis Streams,
// not Marten (different access patterns from mastery events).
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics.Metrics;
using Cena.Actors.Events;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Messaging;

public sealed class ConversationThreadActor : IActor
{
    private readonly IMessageWriter _writer;
    private readonly IMessageReader _reader;
    private readonly IContentModerator _moderator;
    private readonly IMessageThrottler _throttler;
    private readonly IMessagingEventPublisher _eventPublisher;
    private readonly ILogger<ConversationThreadActor> _logger;

    // ── State (loaded from Redis on activation) ──
    private string _threadId = "";
    private string _threadType = "";
    private string[] _participantIds = Array.Empty<string>();
    private string[] _participantNames = Array.Empty<string>();
    private bool _threadExists;

    // ── Telemetry ──
    private static readonly Meter Meter = new("Cena.Actors.Messaging", "1.0.0");
    private static readonly Counter<long> MessagesSent =
        Meter.CreateCounter<long>("cena.messaging.sent_total");
    private static readonly Counter<long> MessagesBlocked =
        Meter.CreateCounter<long>("cena.messaging.blocked_total");

    public ConversationThreadActor(
        IMessageWriter writer,
        IMessageReader reader,
        IContentModerator moderator,
        IMessageThrottler throttler,
        IMessagingEventPublisher eventPublisher,
        ILogger<ConversationThreadActor> logger)
    {
        _writer = writer;
        _reader = reader;
        _moderator = moderator;
        _throttler = throttler;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            SendMessage msg => HandleSendMessage(msg, context),
            AcknowledgeMessage msg => HandleAcknowledge(msg, context),
            GetThreadHistory msg => HandleGetHistory(msg, context),
            MuteThread msg => HandleMuteThread(msg, context),
            _ => Task.CompletedTask
        };
    }

    private async Task HandleSendMessage(SendMessage msg, IContext context)
    {
        _threadId = msg.ThreadId;

        // 1. Validate content length
        if (msg.Content.Text.Length > 2000)
        {
            context.Respond(new MessagingResult(false, "MESSAGE_TOO_LONG",
                "Message exceeds 2000 character limit"));
            return;
        }

        // 2. Check throttle (cheap check first)
        var throttleResult = _throttler.Check(msg.SenderId, msg.SenderRole);
        if (!throttleResult.Allowed)
        {
            context.Respond(new MessagingResult(false, "RATE_LIMIT_EXCEEDED",
                $"Rate limit exceeded. Retry after {throttleResult.RetryAfterSeconds}s"));
            return;
        }

        // 3. Content moderation
        var modResult = _moderator.Check(msg.Content.Text);
        if (!modResult.Safe)
        {
            // Publish blocked event for audit
            await _eventPublisher.PublishMessageBlockedAsync(
                new MessageBlocked_V1(msg.ThreadId, msg.SenderId,
                    modResult.Reason!, DateTimeOffset.UtcNow));

            MessagesBlocked.Add(1, new KeyValuePair<string, object?>("reason", modResult.Reason));

            context.Respond(new MessagingResult(false, "MESSAGE_BLOCKED",
                $"Message blocked: {modResult.Reason}"));
            return;
        }

        // 4. Generate message ID
        var messageId = Guid.NewGuid().ToString("N");

        // 5. Determine recipients
        var recipientIds = ResolveRecipients(msg);

        // 6. Create thread if it doesn't exist
        if (!_threadExists)
        {
            var allParticipants = new HashSet<string> { msg.SenderId };
            if (msg.RecipientId != null) allParticipants.Add(msg.RecipientId);
            foreach (var r in recipientIds) allParticipants.Add(r);

            _participantIds = allParticipants.ToArray();
            _participantNames = _participantIds; // Names resolved by caller
            _threadType = msg.RecipientId == null ? "ClassBroadcast" : "DirectMessage";
            if (msg.SenderRole == MessageRole.Parent) _threadType = "ParentThread";

            var threadCreated = new ThreadCreated_V1(
                msg.ThreadId, _threadType, _participantIds, _participantNames,
                ClassRoomId: null, msg.SenderId, DateTimeOffset.UtcNow);

            await _eventPublisher.PublishThreadCreatedAsync(threadCreated);
            _threadExists = true;
        }

        // 7. Write to Redis Stream
        var entry = new MessageEntry(
            messageId, msg.SenderId, msg.SenderRole, msg.SenderId,
            msg.Content.Text, msg.Content.ContentType,
            msg.Content.ResourceUrl, msg.ReplyToMessageId,
            msg.Channel, DateTimeOffset.UtcNow);

        await _writer.WriteMessageAsync(msg.ThreadId, entry, recipientIds);

        // 8. Publish to NATS for audit
        var sentEvent = new MessageSent_V1(
            msg.ThreadId, messageId, msg.SenderId, msg.SenderRole,
            msg.Content, msg.Channel, DateTimeOffset.UtcNow,
            msg.ReplyToMessageId);

        await _eventPublisher.PublishMessageSentAsync(sentEvent);

        // 9. Record send for throttling
        _throttler.RecordSend(msg.SenderId, msg.SenderRole);

        MessagesSent.Add(1,
            new KeyValuePair<string, object?>("role", msg.SenderRole.ToString()),
            new KeyValuePair<string, object?>("channel", msg.Channel.ToString()));

        _logger.LogInformation(
            "Message sent: thread={ThreadId}, sender={SenderId}, role={Role}",
            msg.ThreadId, msg.SenderId, msg.SenderRole);

        context.Respond(new MessagingResult(true,
            MessageId: messageId, ThreadId: msg.ThreadId));
    }

    private async Task HandleAcknowledge(AcknowledgeMessage msg, IContext context)
    {
        await _reader.MarkReadAsync(msg.ThreadId, msg.ReadById);

        await _eventPublisher.PublishMessageReadAsync(
            new MessageRead_V1(msg.ThreadId, msg.MessageId,
                msg.ReadById, DateTimeOffset.UtcNow));

        context.Respond(new MessagingResult(true));
    }

    private async Task HandleGetHistory(GetThreadHistory msg, IContext context)
    {
        var page = await _reader.GetMessagesAsync(
            msg.ThreadId, msg.BeforeTimestamp, msg.Limit);

        context.Respond(page);
    }

    private async Task HandleMuteThread(MuteThread msg, IContext context)
    {
        var evt = new ThreadMuted_V1(msg.ThreadId, msg.UserId, msg.MutedUntil);
        await _eventPublisher.PublishThreadMutedAsync(evt);

        context.Respond(new MessagingResult(true));
    }

    private static string[] ResolveRecipients(SendMessage msg)
    {
        if (msg.RecipientId != null)
            return new[] { msg.RecipientId };

        // Class broadcast: recipients resolved by the caller/hub
        return Array.Empty<string>();
    }
}
