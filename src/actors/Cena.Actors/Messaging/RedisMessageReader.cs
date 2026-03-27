// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Redis Message Reader
// Layer: Infrastructure | Runtime: .NET 9
// Reads messages from Redis Streams with cursor-based pagination.
// ═══════════════════════════════════════════════════════════════════════

using StackExchange.Redis;

namespace Cena.Actors.Messaging;

public interface IMessageReader
{
    Task<MessagePage> GetMessagesAsync(string threadId, string? beforeCursor, int limit = 20);
    Task<int> GetUnreadCountAsync(string threadId, string userId);
    Task MarkReadAsync(string threadId, string userId);
    Task<string[]> GetUserThreadsAsync(string userId, int offset, int limit);
}

/// <summary>
/// A page of messages with cursor for next page.
/// </summary>
public sealed record MessagePage(
    MessageView[] Messages,
    string? NextCursor,
    bool HasMore
);

/// <summary>
/// A single message view returned from reads.
/// </summary>
public sealed record MessageView(
    string MessageId,
    string SenderId,
    string SenderRole,
    string SenderName,
    string ContentText,
    string ContentType,
    string? ResourceUrl,
    string? ReplyToMessageId,
    string Channel,
    DateTimeOffset SentAt,
    string StreamEntryId
);

public sealed class RedisMessageReader : IMessageReader
{
    private readonly IConnectionMultiplexer _redis;

    public RedisMessageReader(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<MessagePage> GetMessagesAsync(string threadId, string? beforeCursor, int limit = 20)
    {
        if (limit < 1) limit = 1;
        if (limit > 50) limit = 50;

        var db = _redis.GetDatabase();
        var streamKey = MessagingRedisKeys.ThreadStream(threadId);

        // Request one extra to determine hasMore
        int fetchCount = limit + 1;

        // XREVRANGE key max min COUNT n
        // max = beforeCursor (exclusive) or "+" (latest)
        // We need exclusive "before", so we use the entry ID - 1
        var max = string.IsNullOrEmpty(beforeCursor) ? "+" : $"({beforeCursor}";
        var min = "-";

        // StackExchange.Redis XREVRANGE: returns entries in reverse chronological order
        var entries = await db.StreamRangeAsync(
            streamKey,
            minId: min,
            maxId: beforeCursor ?? "+",
            count: fetchCount,
            messageOrder: Order.Descending);

        if (entries.Length == 0)
            return new MessagePage(Array.Empty<MessageView>(), null, false);

        bool hasMore = entries.Length > limit;
        var resultEntries = hasMore ? entries[..limit] : entries;

        var messages = new MessageView[resultEntries.Length];
        for (int i = 0; i < resultEntries.Length; i++)
        {
            var entry = resultEntries[i];
            var vals = entry.Values.ToDictionary(
                v => v.Name.ToString(),
                v => v.Value.ToString());

            messages[i] = new MessageView(
                MessageId: vals.GetValueOrDefault("messageId", ""),
                SenderId: vals.GetValueOrDefault("senderId", ""),
                SenderRole: vals.GetValueOrDefault("senderRole", "System"),
                SenderName: vals.GetValueOrDefault("senderName", ""),
                ContentText: vals.GetValueOrDefault("contentText", ""),
                ContentType: vals.GetValueOrDefault("contentType", "text"),
                ResourceUrl: NullIfEmpty(vals.GetValueOrDefault("resourceUrl", "")),
                ReplyToMessageId: NullIfEmpty(vals.GetValueOrDefault("replyTo", "")),
                Channel: vals.GetValueOrDefault("channel", "InApp"),
                SentAt: DateTimeOffset.TryParse(vals.GetValueOrDefault("sentAt", ""), out var dt)
                    ? dt : DateTimeOffset.UtcNow,
                StreamEntryId: entry.Id.ToString());
        }

        string? nextCursor = hasMore ? resultEntries[^1].Id.ToString() : null;

        return new MessagePage(messages, nextCursor, hasMore);
    }

    public async Task<int> GetUnreadCountAsync(string threadId, string userId)
    {
        var db = _redis.GetDatabase();
        var val = await db.StringGetAsync(MessagingRedisKeys.Unread(threadId, userId));
        return val.IsNull ? 0 : (int)val;
    }

    public async Task MarkReadAsync(string threadId, string userId)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(MessagingRedisKeys.Unread(threadId, userId), 0);
    }

    public async Task<string[]> GetUserThreadsAsync(string userId, int offset, int limit)
    {
        var db = _redis.GetDatabase();
        var entries = await db.SortedSetRangeByRankAsync(
            MessagingRedisKeys.UserThreads(userId),
            start: offset,
            stop: offset + limit - 1,
            order: Order.Descending);

        return entries.Select(e => e.ToString()).ToArray();
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;
}
