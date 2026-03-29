// =============================================================================
// Cena Platform -- Messaging Admin DTOs
// ADM-025: Response types for admin messaging/chat endpoints
// =============================================================================

namespace Cena.Admin.Api;

// Paginated thread list
public sealed record MessagingThreadListResponse(
    IReadOnlyList<MessagingThreadDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

// Thread summary row
public sealed record MessagingThreadDto(
    string ThreadId,
    string ThreadType,       // "DirectMessage", "ClassBroadcast", "ParentThread"
    string[] ParticipantIds,
    string[] ParticipantNames,
    string? ClassRoomId,
    string LastMessagePreview,
    DateTimeOffset LastMessageAt,
    int MessageCount,
    DateTimeOffset CreatedAt);

// Thread detail with message history
public sealed record MessagingThreadDetailDto(
    string ThreadId,
    string ThreadType,
    string[] ParticipantIds,
    string[] ParticipantNames,
    IReadOnlyList<MessagingMessageDto> Messages,
    int TotalMessages,
    string? NextCursor);

// Individual message
public sealed record MessagingMessageDto(
    string MessageId,
    string SenderId,
    string SenderName,
    string SenderRole,       // "Teacher", "Parent", "Student", "System"
    string Text,
    string ContentType,      // "text", "resource-link", "encouragement"
    string? ResourceUrl,
    string Channel,          // "InApp", "WhatsApp", "Telegram", "Push"
    string? ReplyToMessageId,
    bool? WasBlocked,
    string? BlockReason,
    DateTimeOffset SentAt);

// Create thread request
public sealed record CreateThreadRequest(
    string ThreadType,       // "DirectMessage", "ClassBroadcast", "ParentThread"
    string[] ParticipantIds,
    string? ClassRoomId,
    string? InitialMessage);

// Send message request
public sealed record SendMessageRequest(
    string Text,
    string? ContentType,     // defaults to "text"
    string? ResourceUrl,
    string? ReplyToMessageId);

// Contact list for thread creation
public sealed record MessagingContactDto(
    string UserId,
    string DisplayName,
    string Role,             // "Teacher", "Parent", "Student"
    string? Email,
    string? AvatarUrl);

public sealed record MessagingContactListResponse(
    IReadOnlyList<MessagingContactDto> Items);
