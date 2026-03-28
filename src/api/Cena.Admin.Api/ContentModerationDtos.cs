// =============================================================================
// Cena Platform -- Content Moderation DTOs
// ADM-005: Content moderation queue and review workflows
// =============================================================================

namespace Cena.Admin.Api;

// Moderation Queue Item
public sealed record ModerationQueueItem(
    string Id,
    string QuestionText,
    string Subject,
    string Grade,
    string Author,
    DateTimeOffset SubmittedAt,
    ModerationStatus Status,
    string? AssignedTo,
    int AiQualityScore,
    string SourceType  // authored, ingested
);

public enum ModerationStatus
{
    Pending,
    InReview,
    Approved,
    Rejected,
    Flagged
}

// Queue Response
public sealed record ModerationQueueResponse(
    IReadOnlyList<ModerationQueueItem> Items,
    int Total,
    int Page,
    int PageSize);

// Question Detail for Review
public sealed record QuestionDetailResponse(
    string Id,
    string QuestionText,
    IReadOnlyList<AnswerOption> Options,
    string CorrectAnswer,
    string Subject,
    string Topic,
    string Grade,
    string Difficulty,
    IReadOnlyList<string> ConceptTags,
    int AiQualityScore,
    QualityBreakdown QualityScores,
    string? OriginalSource,
    string? NormalizedVersion,
    ModerationStatus Status,
    string? AssignedTo,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<ModerationComment> Comments,
    IReadOnlyList<ModerationHistory> History);

public sealed record AnswerOption(
    string Label,
    string Text,
    bool IsCorrect);

public sealed record QualityBreakdown(
    int MathCorrectness,
    int LanguageQuality,
    int PedagogicalQuality,
    int PlagiarismScore);

public sealed record ModerationComment(
    string Id,
    string Author,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record ModerationHistory(
    DateTimeOffset Timestamp,
    string User,
    string Action,
    string? Reason);

// Moderation Actions
public sealed record ClaimItemRequest(string ModeratorId);
public sealed record RejectItemRequest(string Reason);
public sealed record FlagItemRequest(string Reason);
public sealed record AddCommentRequest(string Text);
public sealed record BulkModerationRequest(string Action, IReadOnlyList<string> ItemIds);

// Moderation Statistics
public sealed record ModerationStatsResponse(
    int Pending,
    int InReview,
    int ApprovedToday,
    int RejectedToday,
    float PendingChange,      // % change vs yesterday
    float InReviewChange,
    float ApprovedTodayChange,
    float RejectedTodayChange,
    int ReviewedThisWeek,
    float ApprovalRate,
    float AvgReviewTimeMinutes,
    IReadOnlyList<PerModeratorStats> PerModeratorStats,
    IReadOnlyList<DailyModerationStats> Trend);

public sealed record PerModeratorStats(
    string ModeratorId,
    string ModeratorName,
    int ItemsReviewed,
    int Approved,
    int Rejected,
    float AvgReviewTimeMinutes);

public sealed record DailyModerationStats(
    string Date,
    int Submitted,
    int Reviewed,
    int Approved,
    int Rejected);

// Queue Summary
public sealed record QueueSummary(
    int TotalPending,
    int InReview,
    int Flagged,
    int OldestPendingHours);
