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
    ContentExtraction,
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

    // ADR-0058: S3 provenance. Populated only when the item was ingested
    // via the S3 cloud-directory provider; null for local / upload /
    // photo sources. Enables fast list-time dedup via ETag match without
    // a GetObject round-trip.
    public string? S3Bucket { get; set; }
    public string? S3ETag { get; set; }

    public PipelineStage CurrentStage { get; set; } = PipelineStage.Incoming;
    public string Status { get; set; } = "processing";     // processing, completed, failed

    // Stage history
    public List<StageRecord> StageHistory { get; set; } = new();

    // OCR results
    public OcrResult? Ocr { get; set; }

    // Extracted questions (item IDs created in the Question aggregate)
    public List<string> ExtractedQuestionIds { get; set; } = new();
    public int ExtractedQuestionCount { get; set; }

    // Extracted content blocks (SAI-05: definitions, theorems, examples, etc.)
    public List<string> ExtractedContentBlockIds { get; set; } = new();
    public int ExtractedContentBlockCount { get; set; }
    public Dictionary<string, int> ContentBlockTypeCounts { get; set; } = new();
    public List<string> LinkedConceptIds { get; set; } = new();

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

    // RDY-019e-IMPL (Phase 1C): CuratorMetadata handshake.
    // The auto-extractor runs on upload and fills AutoExtractedMetadata;
    // the curator reviews via the admin UI and writes CuratorMetadata
    // through PATCH /metadata. MetadataState transitions:
    //
    //   pending          — item registered, extractor hasn't run yet
    //   auto_extracted   — extractor produced values, curator hasn't seen them
    //   awaiting_review  — curator opened the item; one or more fields missing
    //   confirmed        — all required fields set; cascade may use the hints
    //   skipped          — curator explicitly opted out of the handshake
    //
    // Kept as string for Marten schema stability; validated in the service.
    public PipelineCuratorMetadata? AutoExtractedMetadata { get; set; }
    public PipelineCuratorMetadata? CuratorMetadata { get; set; }
    public string MetadataState { get; set; } = "pending";
    public string? MetadataExtractionStrategy { get; set; }
    public Dictionary<string, double> MetadataFieldConfidences { get; set; } = new();
    public DateTimeOffset? MetadataConfirmedAt { get; set; }
    public string? MetadataConfirmedBy { get; set; }
}

/// <summary>
/// Persisted shape of CuratorMetadata (document side). Contract DTO
/// <c>Cena.Api.Contracts.Admin.Ingestion.CuratorMetadata</c> projects onto
/// this and vice versa. Kept as a class rather than a record so Marten can
/// evolve the schema additively (record structural typing is brittle across
/// Marten upcasts).
/// </summary>
public sealed class PipelineCuratorMetadata
{
    public string? Subject { get; set; }
    public string? Language { get; set; }
    public string? Track { get; set; }
    public string? SourceType { get; set; }
    public string? TaxonomyNode { get; set; }
    public bool?   ExpectedFigures { get; set; }
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
