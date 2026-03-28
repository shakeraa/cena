// =============================================================================
// Cena Platform — Ingestion Pipeline Domain Events
// Track file processing through OCR → Segmentation → Normalization → etc.
// =============================================================================

namespace Cena.Actors.Ingest;

/// <summary>File received and registered in the pipeline.</summary>
public sealed record FileReceived_V1(
    string PipelineItemId,
    string SourceFilename,
    string SourceType,
    string? SourceUrl,
    string S3Key,
    string ContentHash,
    string ContentType,
    long FileSizeBytes,
    string SubmittedBy,
    DateTimeOffset Timestamp);

/// <summary>OCR processing completed for the file.</summary>
public sealed record OcrCompleted_V1(
    string PipelineItemId,
    string ModelUsed,
    bool FallbackUsed,
    float Confidence,
    string DetectedLanguage,
    int PageCount,
    decimal CostUsd,
    DateTimeOffset Timestamp);

/// <summary>Individual questions segmented from OCR output.</summary>
public sealed record QuestionsSegmented_V1(
    string PipelineItemId,
    int QuestionCount,
    IReadOnlyList<string> QuestionIds,
    DateTimeOffset Timestamp);

/// <summary>All extracted questions normalized into CenaItem schema.</summary>
public sealed record QuestionsNormalized_V1(
    string PipelineItemId,
    int NormalizedCount,
    DateTimeOffset Timestamp);

/// <summary>Quality classification completed for all questions.</summary>
public sealed record QuestionsClassified_V1(
    string PipelineItemId,
    float AvgQualityScore,
    int PassedCount,
    int FailedCount,
    DateTimeOffset Timestamp);

/// <summary>Deduplication completed.</summary>
public sealed record DeduplicationCompleted_V1(
    string PipelineItemId,
    int DuplicateCount,
    int UniqueCount,
    int NearDuplicateCount,
    DateTimeOffset Timestamp);

/// <summary>Original questions re-created from extracted patterns.</summary>
public sealed record QuestionsRecreated_V1(
    string PipelineItemId,
    int RecreatedCount,
    IReadOnlyList<string> RecreatedQuestionIds,
    DateTimeOffset Timestamp);

/// <summary>Semantic content block extracted from source document (non-question content).</summary>
public sealed record ContentExtracted_V1(
    string ContentBlockId,
    string SourceDocId,
    string ContentType,             // "definition", "theorem", "example", "explanation", "exercise_solution"
    string RawText,
    string ProcessedText,           // Cleaned, structured (Markdown)
    IReadOnlyList<string> ConceptIds,
    string Language,                // "he", "ar", "en"
    string? PageRange,              // "12-13" from source doc
    string Subject,
    string Topic,
    DateTimeOffset Timestamp);

/// <summary>Pipeline processing failed at a stage.</summary>
public sealed record PipelineStageFailed_V1(
    string PipelineItemId,
    PipelineStage FailedStage,
    string ErrorMessage,
    int RetryCount,
    DateTimeOffset Timestamp);

/// <summary>Item moved to moderation review queue.</summary>
public sealed record MovedToReview_V1(
    string PipelineItemId,
    int QuestionCount,
    DateTimeOffset Timestamp);

/// <summary>Pipeline processing completed (all stages done).</summary>
public sealed record PipelineCompleted_V1(
    string PipelineItemId,
    int TotalExtracted,
    int TotalPublished,
    int TotalDuplicates,
    int TotalRecreated,
    DateTimeOffset Timestamp);
