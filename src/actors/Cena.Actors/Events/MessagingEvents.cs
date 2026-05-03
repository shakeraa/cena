// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging Context Domain Events
// Layer: Domain Events | Runtime: .NET 9
// Thread-level events persisted via Marten (metadata only).
// Individual messages stored in Redis Streams, not Marten.
// ═══════════════════════════════════════════════════════════════════════

using Cena.Actors.Messaging;

namespace Cena.Actors.Events;

/// <summary>
/// A new conversation thread was created. Persisted in Marten for
/// the ThreadSummary projection.
/// </summary>
public record ThreadCreated_V1(
    string ThreadId,
    string ThreadType,
    string[] ParticipantIds,
    string[] ParticipantNames,
    string? ClassRoomId,
    string CreatedById,
    DateTimeOffset CreatedAt
);

/// <summary>
/// A participant muted/unmuted a thread.
/// </summary>
public record ThreadMuted_V1(
    string ThreadId,
    string UserId,
    DateTimeOffset? MutedUntil
);

/// <summary>
/// A human message was sent. Published to NATS for audit.
/// The actual message content is stored in Redis Streams.
/// </summary>
public record MessageSent_V1(
    string ThreadId,
    string MessageId,
    string SenderId,
    MessageRole SenderRole,
    MessageContent Content,
    MessageChannel Channel,
    DateTimeOffset SentAt,
    string? ReplyToMessageId
);

/// <summary>
/// A message was marked as read by a participant.
/// </summary>
public record MessageRead_V1(
    string ThreadId,
    string MessageId,
    string ReadById,
    DateTimeOffset ReadAt
);

/// <summary>
/// A message was blocked by content moderation.
/// </summary>
public record MessageBlocked_V1(
    string ThreadId,
    string SenderId,
    string Reason,
    DateTimeOffset BlockedAt
);
