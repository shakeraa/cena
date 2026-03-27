// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging Actor Proto.Actor Messages
// Layer: Actor Messages | Runtime: .NET 9
// Command messages handled by ConversationThreadActor.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Messaging;

/// <summary>
/// Send a message in a conversation thread. Creates the thread if it
/// doesn't exist yet (atomic thread creation + first message).
/// </summary>
public record SendMessage(
    string ThreadId,
    string SenderId,
    string SenderName,
    MessageRole SenderRole,
    string? RecipientId,
    string? RecipientName,
    MessageContent Content,
    MessageChannel Channel,
    string? ReplyToMessageId
);

/// <summary>
/// Mark a specific message as read by a participant.
/// </summary>
public record AcknowledgeMessage(
    string ThreadId,
    string MessageId,
    string ReadById
);

/// <summary>
/// Retrieve paginated message history for a thread.
/// </summary>
public record GetThreadHistory(
    string ThreadId,
    string? BeforeTimestamp,
    int Limit = 20
);

/// <summary>
/// Mute or unmute a thread for a participant.
/// </summary>
public record MuteThread(
    string ThreadId,
    string UserId,
    DateTimeOffset? MutedUntil
);

/// <summary>
/// Standard result envelope for actor command responses.
/// Reuses the pattern from existing actor-contracts.
/// </summary>
public sealed record MessagingResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? MessageId = null,
    string? ThreadId = null
);
