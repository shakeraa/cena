// =============================================================================
// Cena Platform -- Notifications API DTOs (STB-07 Phase 1)
// Notifications list, unread count, and read operations
// =============================================================================

namespace Cena.Api.Contracts.Notifications;

public record NotificationListDto(
    NotificationItem[] Items,
    int Page,
    int PageSize,
    int Total,
    bool HasMore,
    int UnreadCount);

public record NotificationItem(
    string NotificationId,
    string Kind,          // 'xp' | 'badge' | 'streak' | 'friend-request' | 'review-due' | 'system'
    string Priority,      // 'low' | 'normal' | 'high'
    string Title,
    string Body,
    string? IconName,
    string? DeepLinkUrl,  // e.g. '/progress' for a badge notification
    bool Read,
    DateTime CreatedAt);

public record UnreadCountDto(int Count);

public record MarkReadResponse(bool Ok, string Id);

public record MarkAllReadResponse(bool Ok, int MarkedCount);
