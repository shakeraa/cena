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

// ═════════════════════════════════════════════════════════════════════════════
// FIND-privacy-018: Reporting & Blocking Documents
// ICO Children's Code Std 11 — safeguarding tools for minors
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A report filed by a student against social content. Feeds the back-office
/// moderation queue where safeguarding admins review, resolve, or escalate.
/// </summary>
public class SocialReportDocument
{
    public string Id { get; set; } = "";
    public string ReportId { get; set; } = "";
    public string ReporterStudentId { get; set; } = "";
    public string ContentType { get; set; } = "";    // 'feed-item' | 'comment' | 'peer-solution' | 'friend-request' | 'study-room'
    public string ContentId { get; set; } = "";
    public string Category { get; set; } = "";        // 'bullying' | 'inappropriate' | 'spam' | 'self-harm-risk' | 'other'
    public string Severity { get; set; } = "medium";  // 'low' | 'medium' | 'high' | 'critical'
    public string? Reason { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "pending";   // 'pending' | 'reviewing' | 'resolved' | 'escalated'
    public string? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Resolution { get; set; }
}

/// <summary>
/// Per-student block list document. Each student has at most one document
/// with an array of blocked student IDs. All social queries filter out
/// content authored by blocked students.
/// </summary>
public class UserBlocklistDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public List<BlockedEntry> BlockedUsers { get; set; } = new();
}

/// <summary>
/// An individual entry in a user's block list.
/// </summary>
public class BlockedEntry
{
    public string BlockedStudentId { get; set; } = "";
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
}
