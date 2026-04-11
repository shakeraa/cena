// =============================================================================
// Cena Platform — Social Documents (STB-06b)
// Comments, friend requests, friendships, and study rooms
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Comment document for class feed items and peer solutions.
/// </summary>
public class CommentDocument
{
    public string Id { get; set; } = "";
    public string CommentId { get; set; } = "";
    public string ItemId { get; set; } = ""; // Feed item or solution ID
    public string AuthorStudentId { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
}

/// <summary>
/// Friend request document for pending friend requests.
/// </summary>
public class FriendRequestDocument
{
    public string Id { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string FromStudentId { get; set; } = "";
    public string ToStudentId { get; set; } = "";
    public string Status { get; set; } = "pending"; // 'pending' | 'accepted' | 'declined' | 'cancelled'
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}

/// <summary>
/// Friendship document representing an established friend relationship.
/// </summary>
public class FriendshipDocument
{
    public string Id { get; set; } = "";
    public string FriendshipId { get; set; } = "";
    public string StudentAId { get; set; } = "";
    public string StudentBId { get; set; } = "";
    public DateTime BecameFriendsAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Study room document for group study sessions.
/// </summary>
public class StudyRoomDocument
{
    public string Id { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HostStudentId { get; set; } = "";
    public bool IsPublic { get; set; } = true;
    public int MaxCapacity { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Study room membership document tracking who is in a room.
/// </summary>
public class StudyRoomMembershipDocument
{
    public string Id { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Class feed item document for social activity feed (achievements, milestones, questions, announcements).
/// </summary>
public class ClassFeedItemDocument
{
    public string Id { get; set; } = "";
    public string FeedItemId { get; set; } = "";
    public string? ClassroomId { get; set; }
    public string Kind { get; set; } = ""; // 'achievement' | 'milestone' | 'question' | 'announcement'
    public string AuthorStudentId { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string? AuthorAvatarUrl { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public int ReactionCount { get; set; }
    public int CommentCount { get; set; }
    public bool IsDeleted { get; set; } = false;
}

/// <summary>
/// Peer solution document for student-contributed answers to questions.
/// </summary>
public class PeerSolutionDocument
{
    public string Id { get; set; } = "";
    public string SolutionId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string AuthorStudentId { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Content { get; set; } = "";
    public int UpvoteCount { get; set; }
    public int DownvoteCount { get; set; }
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
}
