// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Redis Message Writer
// Layer: Infrastructure | Runtime: .NET 9
// Writes messages to Redis Streams with pipeline batching.
// ═══════════════════════════════════════════════════════════════════════

using StackExchange.Redis;

namespace Cena.Actors.Messaging;

public interface IMessageWriter
{
    /// <summary>
    /// Write a message to the Redis Stream for the given thread.
    /// Atomically updates unread counters and user thread lists.
    /// </summary>
    Task<string> WriteMessageAsync(
        string threadId,
        MessageEntry message,
        string[] recipientIds);
}

/// <summary>
/// Flat entry for a Redis Stream message.
/// </summary>
public sealed record MessageEntry(
    string MessageId,
    string SenderId,
    MessageRole SenderRole,
    string SenderName,
    string ContentText,
    string ContentType,
    string? ResourceUrl,
    string? ReplyToMessageId,
    MessageChannel Channel,
    DateTimeOffset SentAt
);

public sealed class RedisMessageWriter : IMessageWriter
{
    private readonly IConnectionMultiplexer _redis;

    public RedisMessageWriter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<string> WriteMessageAsync(
        string threadId,
        MessageEntry message,
        string[] recipientIds)
    {
        var db = _redis.GetDatabase();

        var streamKey = MessagingRedisKeys.ThreadStream(threadId);

        // Build the stream entry fields
        var fields = new NameValueEntry[]
        {
            new("messageId", message.MessageId),
            new("senderId", message.SenderId),
            new("senderRole", message.SenderRole.ToString()),
            new("senderName", message.SenderName),
            new("contentText", message.ContentText.Length > 2000
                ? message.ContentText[..2000]
                : message.ContentText),
            new("contentType", message.ContentType),
            new("resourceUrl", message.ResourceUrl ?? ""),
            new("replyTo", message.ReplyToMessageId ?? ""),
            new("channel", message.Channel.ToString()),
            new("sentAt", message.SentAt.ToString("O")),
        };

        // 1. XADD with approximate trimming
        var entryId = await db.StreamAddAsync(
            streamKey, fields,
            maxLength: MessagingRedisKeys.MaxStreamLength,
            useApproximateMaxLength: true);

        // 2. Refresh TTL
        await db.KeyExpireAsync(streamKey, MessagingRedisKeys.MessageTtl);

        // 3. Increment unread counters for recipients (not the sender)
        var tasks = new List<Task>(recipientIds.Length + recipientIds.Length + 1);

        foreach (var recipientId in recipientIds)
        {
            if (recipientId != message.SenderId)
            {
                tasks.Add(db.StringIncrementAsync(
                    MessagingRedisKeys.Unread(threadId, recipientId)));
            }
        }

        // 4. Update user thread sorted sets (sender + all recipients)
        double score = message.SentAt.ToUnixTimeMilliseconds();

        tasks.Add(db.SortedSetAddAsync(
            MessagingRedisKeys.UserThreads(message.SenderId),
            threadId, score));

        foreach (var recipientId in recipientIds)
        {
            tasks.Add(db.SortedSetAddAsync(
                MessagingRedisKeys.UserThreads(recipientId),
                threadId, score));
        }

        await Task.WhenAll(tasks);

        return entryId.ToString();
    }
}
