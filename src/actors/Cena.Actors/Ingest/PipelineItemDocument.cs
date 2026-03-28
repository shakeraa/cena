// =============================================================================
// Cena Platform — Pipeline Item Marten Document
// Tracks a file/URL through the ingestion pipeline stages.
// Event-sourced via Marten lightweight projections.
// =============================================================================

namespace Cena.Actors.Ingest;

/// <summary>
/// Pipeline stage identifiers matching the ingestion specification.
/// </summary>
public enum PipelineStage
{
    Incoming,
    OcrProcessing,
    Segmented,
    Normalized,
    Classified,
    Deduplicated,
    ReCreated,
    InReview,
    Published,
    Failed
}

/// <summary>
/// Marten document tracking a single file through the ingestion pipeline.
/// Content-addressed: Id = SHA-256 of raw file bytes.
/// </summary>
public sealed class PipelineItemDocument
{
    public string Id { get; set; } = "";
    public string SourceFilename { get; set; } = "";
    public string SourceType { get; set; } = "";           // url, s3, photo, batch
    public string? SourceUrl { get; set; }
    public string S3Key { get; set; } = "";
    public string ContentHash { get; set; } = "";           // SHA-256 of raw file
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }

    public PipelineStage CurrentStage { get; set; } = PipelineStage.Incoming;
    public string Status { get; set; } = "processing";     // processing, completed, failed

    // Stage history
    public List<StageRecord> StageHistory { get; set; } = new();

    // OCR results
    public OcrResult? Ocr { get; set; }

    // Extracted questions (item IDs created in the Question aggregate)
    public List<string> ExtractedQuestionIds { get; set; } = new();
    public int ExtractedQuestionCount { get; set; }

    // Quality summary across extracted questions
    public float? AvgQualityScore { get; set; }
    public int? DuplicateCount { get; set; }
    public int? RecreatedCount { get; set; }

    // Error tracking
    public string? LastError { get; set; }
    public int RetryCount { get; set; }

    // Audit
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StageRecord
{
    public PipelineStage Stage { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "processing";
    public string? ErrorMessage { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
}

public sealed class OcrResult
{
    public string ModelUsed { get; set; } = "";             // gemini-2.5-flash, mathpix
    public bool FallbackUsed { get; set; }
    public float Confidence { get; set; }
    public string DetectedLanguage { get; set; } = "";      // he, ar, en
    public int PageCount { get; set; }
    public List<OcrPageResult> Pages { get; set; } = new();
    public decimal CostUsd { get; set; }
}

public sealed class OcrPageResult
{
    public int PageNumber { get; set; }
    public string RawText { get; set; } = "";
    public Dictionary<string, string> MathExpressions { get; set; } = new(); // key → LaTeX
    public float Confidence { get; set; }
}
