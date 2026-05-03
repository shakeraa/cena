// =============================================================================
// Cena Platform -- Social API DTOs (STB-06 Phase 1)
// Class feed, peer solutions, friends, and study rooms
// =============================================================================

namespace Cena.Api.Contracts.Social;

// ═════════════════════════════════════════════════════════════════════════════
// Class Feed DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record ClassFeedDto(
    ClassFeedItem[] Items,
    int Page,
    int PageSize,
    bool HasMore);

public record ClassFeedItem(
    string ItemId,
    string Kind,         // 'achievement' | 'milestone' | 'question' | 'announcement'
    string AuthorStudentId,
    string AuthorDisplayName,
    string? AuthorAvatarUrl,
    string Title,
    string? Body,
    DateTime PostedAt,
    int ReactionCount,
    int CommentCount);

// ═════════════════════════════════════════════════════════════════════════════
// Peer Solutions DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record PeerSolutionListDto(PeerSolution[] Solutions);

public record PeerSolution(
    string SolutionId,
    string QuestionId,
    string AuthorStudentId,
    string AuthorDisplayName,
    string Content,
    int UpvoteCount,
    int DownvoteCount,
    DateTime PostedAt);

// ═════════════════════════════════════════════════════════════════════════════
// Friends DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record FriendsListDto(
    Friend[] Friends,
    FriendRequest[] PendingRequests);

public record Friend(
    string StudentId,
    string DisplayName,
    string? AvatarUrl,
    int Level,
    int StreakDays,
    bool IsOnline);

public record FriendRequest(
    string RequestId,
    string FromStudentId,
    string FromDisplayName,
    string? FromAvatarUrl,
    DateTime RequestedAt);

// ═════════════════════════════════════════════════════════════════════════════
// Study Rooms DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record StudyRoomListDto(StudyRoom[] Rooms);

public record StudyRoom(
    string RoomId,
    string Name,
    string Subject,
    string HostStudentId,
    string HostDisplayName,
    int MemberCount,
    int MaxCapacity,
    bool IsPublic,
    DateTime CreatedAt);

// ═════════════════════════════════════════════════════════════════════════════
// STB-06b: Write Endpoint Request/Response DTOs
// ═════════════════════════════════════════════════════════════════════════════

// Reactions
public record AddReactionRequest(string ItemId, string ReactionType);
public record AddReactionResponse(bool Ok, string ItemId, string ReactionType, int NewCount);

// Comments
public record AddCommentRequest(string ItemId, string Content);
public record AddCommentResponse(string CommentId, string ItemId, string Content, DateTime PostedAt);

// Friend Requests
public record SendFriendRequestRequest(string TargetStudentId);
public record SendFriendRequestResponse(string RequestId, string Status);
public record AcceptFriendRequestResponse(bool Ok, string FriendshipId);

// Study Rooms
public record CreateStudyRoomRequest(string Name, string Subject, bool IsPublic, int MaxCapacity);
public record JoinStudyRoomResponse(bool Ok, string RoomId, int MemberCount);

// ═════════════════════════════════════════════════════════════════════════════
// FIND-privacy-018: Content Reporting & User Blocking DTOs
// ICO Children's Code Std 11 — safeguarding tools for minors
// ═════════════════════════════════════════════════════════════════════════════

// Reporting
public record SubmitReportRequest(
    string ContentType,     // 'feed-item' | 'comment' | 'peer-solution' | 'friend-request' | 'study-room'
    string ContentId,
    string Category,        // 'bullying' | 'inappropriate' | 'spam' | 'self-harm-risk' | 'other'
    string? Reason);
public record SubmitReportResponse(string ReportId, string Severity, DateTime ReportedAt);

// Blocking
public record BlockUserRequest(string TargetStudentId);
public record BlockUserResponse(bool Ok, string TargetStudentId, DateTime BlockedAt);
public record UnblockUserResponse(bool Ok, string TargetStudentId);
