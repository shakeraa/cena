// =============================================================================
// Cena Platform — Social Domain Events (STB-06b)
// Reactions, comments, friend requests, study rooms
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a student adds a reaction to a feed item or solution.
/// </summary>
public record ReactionAdded_V1(
    string StudentId,
    string ItemId,
    string ReactionType,  // 'like' | 'celebrate' | 'helpful' | 'insightful'
    DateTimeOffset AddedAt
);

/// <summary>
/// Emitted when a comment is posted.
/// </summary>
public record CommentPosted_V1(
    string CommentId,
    string ItemId,
    string AuthorStudentId,
    string Content,
    DateTimeOffset PostedAt
);

/// <summary>
/// Emitted when a friend request is sent.
/// </summary>
public record FriendRequestSent_V1(
    string RequestId,
    string FromStudentId,
    string ToStudentId,
    DateTimeOffset RequestedAt
);

/// <summary>
/// Emitted when a friend request is accepted.
/// </summary>
public record FriendRequestAccepted_V1(
    string RequestId,
    string FromStudentId,
    string ToStudentId,
    DateTimeOffset AcceptedAt
);

/// <summary>
/// Emitted when a study room is created.
/// </summary>
public record StudyRoomCreated_V1(
    string RoomId,
    string Name,
    string Subject,
    string HostStudentId,
    bool IsPublic,
    int MaxCapacity,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Emitted when a student joins a study room.
/// </summary>
public record StudyRoomJoined_V1(
    string RoomId,
    string StudentId,
    DateTimeOffset JoinedAt
);

/// <summary>
/// Emitted when a student leaves a study room.
/// </summary>
public record StudyRoomLeft_V1(
    string RoomId,
    string StudentId,
    DateTimeOffset LeftAt
);

// ═════════════════════════════════════════════════════════════════════════════
// FIND-privacy-018: Reporting & Blocking Events
// ICO Children's Code Std 11 — safeguarding tools for minors
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Emitted when a student files a report against social content.
/// Categories: Bullying, Inappropriate, Spam, SelfHarmRisk, Other.
/// </summary>
public record SocialReportFiled_V1(
    string ReportId,
    string ReporterStudentId,
    string ContentType,     // 'feed-item' | 'comment' | 'peer-solution' | 'friend-request' | 'study-room'
    string ContentId,
    string Category,        // 'bullying' | 'inappropriate' | 'spam' | 'self-harm-risk' | 'other'
    string Severity,        // 'low' | 'medium' | 'high' | 'critical'
    string? Reason,
    DateTimeOffset ReportedAt
);

/// <summary>
/// Emitted when a student blocks another student.
/// Blocked user's content is filtered from the blocker's social surfaces.
/// </summary>
public record UserBlocked_V1(
    string BlockerStudentId,
    string BlockedStudentId,
    DateTimeOffset BlockedAt
);

/// <summary>
/// Emitted when a student unblocks a previously blocked student.
/// </summary>
public record UserUnblocked_V1(
    string BlockerStudentId,
    string UnblockedStudentId,
    DateTimeOffset UnblockedAt
);
