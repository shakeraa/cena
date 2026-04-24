// =============================================================================
// Cena Platform — Mentor Chat Documents (TENANCY-P3d)
// Marten documents for mentor-student text messaging via SignalR.
// Distinct from tutor-chat (which is LLM). This is human-to-human.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// TENANCY-P3d: A mentor-student chat channel tied to a classroom enrollment.
/// Privacy: messages are per-enrollment, cross-institute invisible.
/// </summary>
public sealed class MentorChatChannelDocument
{
    public string Id { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string MentorId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string? InstituteId { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public int UnreadByMentor { get; set; }
    public int UnreadByStudent { get; set; }
}

/// <summary>
/// A single chat message in a mentor-student channel.
/// </summary>
public sealed class MentorChatMessageDocument
{
    public string Id { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string SenderRole { get; set; } = ""; // "mentor" | "student"
    public string Content { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool IsRead { get; set; }
}
