// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Composite Message Reader
// Layer: Domain Service | Runtime: .NET 9
// Orchestrates reads from Redis (hot) and S3 (cold) based on message age.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Messaging.Archival;

/// <summary>
/// Combines Redis (hot, 0-30 days) and S3 (cold, 30+ days) message reads.
/// Transparently serves from the correct tier based on cursor timestamp.
/// </summary>
public sealed class CompositeMessageReader : IMessageReader
{
    private readonly IMessageReader _hotReader;
    private readonly S3MessageReader? _coldReader;

    /// <summary>Messages older than this are served from S3.</summary>
    private static readonly TimeSpan ColdThreshold = TimeSpan.FromDays(30);

    public CompositeMessageReader(IMessageReader hotReader, S3MessageReader? coldReader = null)
    {
        _hotReader = hotReader;
        _coldReader = coldReader;
    }

    public async Task<MessagePage> GetMessagesAsync(string threadId, string? beforeCursor, int limit = 20)
    {
        // 1. Parse the cursor to determine if we need cold storage
        DateTimeOffset? cursorTime = null;
        if (!string.IsNullOrEmpty(beforeCursor) && DateTimeOffset.TryParse(beforeCursor, out var parsed))
            cursorTime = parsed;

        bool isColdRange = cursorTime.HasValue
            && cursorTime.Value < DateTimeOffset.UtcNow - ColdThreshold;

        // 2. If cursor is in cold range and we have a cold reader, query S3
        if (isColdRange && _coldReader != null)
        {
            return await _coldReader.GetMessagesAsync(threadId, cursorTime!.Value, limit);
        }

        // 3. Otherwise, query Redis
        var hotPage = await _hotReader.GetMessagesAsync(threadId, beforeCursor, limit);

        // 4. If Redis returns less than requested and no more, check if S3 has older data
        if (!hotPage.HasMore && hotPage.Messages.Length < limit && _coldReader != null)
        {
            var oldestHot = hotPage.Messages.Length > 0
                ? hotPage.Messages[^1].SentAt
                : DateTimeOffset.UtcNow;

            if (oldestHot < DateTimeOffset.UtcNow - ColdThreshold.Subtract(TimeSpan.FromDays(2)))
            {
                var remaining = limit - hotPage.Messages.Length;
                var coldPage = await _coldReader.GetMessagesAsync(threadId, oldestHot, remaining);

                if (coldPage.Messages.Length > 0)
                {
                    var merged = hotPage.Messages.Concat(coldPage.Messages).ToArray();
                    return new MessagePage(merged, coldPage.NextCursor, coldPage.HasMore);
                }
            }
        }

        return hotPage;
    }

    public Task<int> GetUnreadCountAsync(string threadId, string userId) =>
        _hotReader.GetUnreadCountAsync(threadId, userId);

    public Task MarkReadAsync(string threadId, string userId) =>
        _hotReader.MarkReadAsync(threadId, userId);

    public Task<string[]> GetUserThreadsAsync(string userId, int offset, int limit) =>
        _hotReader.GetUserThreadsAsync(userId, offset, limit);
}
