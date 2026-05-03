// =============================================================================
// Cena Platform — Mentor Chat Events (TENANCY-P3d)
// =============================================================================

namespace Cena.Actors.Events;

public record MentorChatMessageSent_V1(
    string ChannelId,
    string SenderId,
    string SenderRole,
    string Content,
    string ClassroomId,
    string? InstituteId,
    DateTimeOffset SentAt
) : IDelegatedEvent;

public record MentorChatMessageRead_V1(
    string ChannelId,
    string MessageId,
    string ReadById,
    DateTimeOffset ReadAt
) : IDelegatedEvent;
