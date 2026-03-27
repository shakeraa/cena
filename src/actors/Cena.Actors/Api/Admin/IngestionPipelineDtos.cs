// =============================================================================
// Cena Platform -- Content Ingestion Pipeline DTOs
// ADM-009: Pipeline dashboard and management
// =============================================================================

namespace Cena.Actors.Api.Admin;

// Pipeline Kanban View
public sealed record PipelineStatusResponse(
    DateTimeOffset Timestamp,
    IReadOnlyList<PipelineStage> Stages);

public sealed record PipelineStage(
    string StageId,
    string Name,
    int Count,
    string Status,  // healthy, slow, failed
    IReadOnlyList<PipelineItem> Items);

public sealed record PipelineItem(
    string Id,
    string SourceFilename,
    string SourceType,  // URL, S3, photo, batch
    int QuestionCount,
    int QualityScore,
    DateTimeOffset Timestamp,
    string? ErrorMessage,
    bool HasError);

// Item Detail
public sealed record PipelineItemDetailResponse(
    string Id,
    string SourceFilename,
    string SourceType,
    string SourceUrl,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    string CurrentStage,
    IReadOnlyList<StageProcessingInfo> StageHistory,
    OcrOutput? OcrResult,
    QualityScores Quality,
    IReadOnlyList<ExtractedQuestion> ExtractedQuestions);

public sealed record StageProcessingInfo(
    string Stage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan? Duration,
    string Status,  // pending, processing, completed, failed
    string? ErrorMessage);

public sealed record OcrOutput(
    string OriginalImageUrl,
    string ExtractedText,
    float Confidence,
    IReadOnlyList<OcrRegion> Regions);

public sealed record OcrRegion(
    int X, int Y, int Width, int Height,
    string Text,
    float Confidence);

public sealed record QualityScores(
    int MathCorrectness,
    int LanguageQuality,
    int PedagogicalQuality,
    int PlagiarismScore);

public sealed record ExtractedQuestion(
    int Index,
    string Text,
    string? Answer,
    float Confidence);

// Pipeline Actions
public sealed record RetryItemRequest(string? Reason);
public sealed record RejectPipelineItemRequest(string Reason);
public sealed record UploadFileResponse(
    string UploadId,
    string Status,
    string? PipelineItemId);
public sealed record SubmitUrlRequest(
    string Url,
    string? SourceType);

// Pipeline Analytics
public sealed record PipelineStatsResponse(
    IReadOnlyList<ThroughputPoint> Throughput,
    IReadOnlyList<StageFailureRate> FailureRates,
    IReadOnlyList<AvgProcessingTime> ProcessingTimes,
    QueueDepthTrend QueueTrend);

public sealed record ThroughputPoint(
    string Period,  // hour or day
    int ItemsProcessed);

public sealed record StageFailureRate(
    string Stage,
    float FailureRate,
    int TotalProcessed,
    int FailedCount);

public sealed record AvgProcessingTime(
    string Stage,
    float AvgSeconds,
    float P95Seconds);

public sealed record QueueDepthTrend(
    IReadOnlyList<DepthPoint> Points);

public sealed record DepthPoint(
    string Timestamp,
    int Incoming,
    int Processing,
    int Completed);
