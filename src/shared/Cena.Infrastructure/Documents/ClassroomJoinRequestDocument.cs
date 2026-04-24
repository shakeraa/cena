// =============================================================================
// Cena Platform — Classroom Join Request Document (TENANCY-P2b)
// Marten document for ManualApprove mode join requests.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Status of a classroom join request.
/// </summary>
public enum JoinRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Expired
}

/// <summary>
/// TENANCY-P2b: Join request for ManualApprove classrooms.
/// Created when a student attempts to join a ManualApprove or InviteOnly classroom.
/// Mentor/teacher reviews and approves or rejects.
/// </summary>
public sealed class ClassroomJoinRequestDocument
{
    public string Id { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string? StudentName { get; set; }
    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public string? ReviewedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Invite code used (for InviteOnly classrooms). Null for join-code requests.
    /// </summary>
    public string? InviteCode { get; set; }
}
