// =============================================================================
// Cena Platform — Moderation Audit Trail (Marten Document)
// Records every moderator action for accountability and analytics.
// Separate from QuestionState events to enable fast moderation-specific queries.
// =============================================================================

namespace Cena.Actors.Ingest;

/// <summary>
/// Marten document tracking moderation actions per question.
/// Indexed for fast queue queries and moderator performance analytics.
/// </summary>
public sealed class ModerationAuditDocument
{
    public string Id { get; set; } = "";        // Same as QuestionId
    public string QuestionId { get; set; } = "";
    public ModerationItemStatus Status { get; set; } = ModerationItemStatus.Pending;
    public string? AssignedTo { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ClaimExpiresAt { get; set; }    // Auto-release after 30 min
    public string Priority { get; set; } = "standard";     // high, standard, spot_check
    public string SourceType { get; set; } = "authored";   // authored, ingested, recreated, batch_generated
    public int AiQualityScore { get; set; }

    // Question preview data (denormalized for queue listing)
    public string StemPreview { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Language { get; set; } = "he";
    public string CreatedBy { get; set; } = "";

    // Rejection tracking
    public string? RejectionReason { get; set; }
    public int RejectionCount { get; set; }

    // Audit trail
    public List<ModerationActionRecord> Actions { get; set; } = new();
    public List<ModerationCommentRecord> Comments { get; set; } = new();

    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ModerationItemStatus
{
    Pending,
    InReview,
    Approved,
    ApprovedWithEdits,
    Rejected,
    Flagged,
    AutoApproved
}

public sealed class ModerationActionRecord
{
    public string Action { get; set; } = "";        // claim, approve, reject, flag, escalate, request_changes, release
    public string ModeratorId { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class ModerationCommentRecord
{
    public string Id { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
