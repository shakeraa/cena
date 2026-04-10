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
