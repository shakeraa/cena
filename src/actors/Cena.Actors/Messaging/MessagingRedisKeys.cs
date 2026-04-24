// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging Redis Key Schema
// Layer: Infrastructure | Runtime: .NET 9
// Centralizes all Redis key patterns for the Messaging context.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Messaging;

/// <summary>
/// Redis key constants for the Messaging context. No magic strings.
/// </summary>
public static class MessagingRedisKeys
{
    private const string Prefix = "cena";

    /// <summary>Redis Stream for messages in a thread.</summary>
    public static string ThreadStream(string threadId) => $"{Prefix}:thread:{threadId}";

    /// <summary>Hash containing thread metadata (type, participants, createdAt).</summary>
    public static string ThreadMeta(string threadId) => $"{Prefix}:thread:{threadId}:meta";

    /// <summary>Unread counter per participant per thread.</summary>
    public static string Unread(string threadId, string userId) =>
        $"{Prefix}:thread:{threadId}:unread:{userId}";

    /// <summary>Sorted set of thread IDs scored by lastMessageAt epoch ms.</summary>
    public static string UserThreads(string userId) => $"{Prefix}:user:{userId}:threads";

    /// <summary>Idempotency key for webhook deduplication (5 min TTL).</summary>
    public static string WebhookDedup(string source, string externalId) =>
        $"{Prefix}:webhook:dedup:{source}:{externalId}";

    /// <summary>Default TTL for message streams: 30 days.</summary>
    public static readonly TimeSpan MessageTtl = TimeSpan.FromDays(30);

    /// <summary>Deduplication TTL: 5 minutes.</summary>
    public static readonly TimeSpan DeduplicationTtl = TimeSpan.FromMinutes(5);

    /// <summary>Approximate max stream length (MAXLEN ~).</summary>
    public const int MaxStreamLength = 10_000;
}
