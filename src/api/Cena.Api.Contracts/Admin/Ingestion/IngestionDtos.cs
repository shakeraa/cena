// =============================================================================
// Cena Platform -- Content Ingestion Pipeline DTOs
// ADM-009: Pipeline dashboard and management
// RDY-019e-IMPL (Phase 1C): CuratorMetadata handshake DTOs
// =============================================================================

namespace Cena.Api.Contracts.Admin.Ingestion;

// ---------------------------------------------------------------------------
// CuratorMetadata handshake (RDY-019e-IMPL).
//
// These records map 1:1 to Cena.Infrastructure.Ocr.Contracts.OcrContextHints.
// The admin UI captures them during the review handshake (or the extractor
// auto-fills them); the cascade then consumes the confirmed values when
// recognising the item.
//
// Field names are snake_case on the wire to match the Python fixtures in
// scripts/ocr-spike/dev-fixtures/context-hints/examples.json.
// ---------------------------------------------------------------------------
public sealed record CuratorMetadata(
    string? Subject,           // "math" | "physics" | ...
    string? Language,          // "he" | "en" | "ar"
    string? Track,             // "3u" | "4u" | "5u"
    string? SourceType,        // matches OcrContextHints.SourceType enum value (lowercase_snake)
    string? TaxonomyNode,      // e.g. "algebra.polynomials"
    bool?   ExpectedFigures);

public sealed record AutoExtractedMetadata(
    CuratorMetadata Extracted,
    IReadOnlyDictionary<string, double> FieldConfidences,
    string ExtractionStrategy);    // "filename" | "pdf_metadata" | "one_page_preview" | "combined"

public sealed record CuratorMetadataResponse(
    string ItemId,
    string MetadataState,          // "pending" | "auto_extracted" | "awaiting_review" | "confirmed" | "skipped"
    AutoExtractedMetadata? AutoExtracted,
    CuratorMetadata? Current,
    IReadOnlyList<string> MissingRequired);

/// <summary>
/// Partial PATCH body — only non-null fields are applied; null means "leave alone".
/// Explicit field clearance uses the DELETE /metadata/{field} endpoint.
/// </summary>
public sealed record CuratorMetadataPatch(
    string? Subject = null,
    string? Language = null,
    string? Track = null,
    string? SourceType = null,
    string? TaxonomyNode = null,
    bool?   ExpectedFigures = null);

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
//
// Visual-review fields (added 2026-05-01):
//   HasSourcePdf — true when the source PDF is retrievable via
//                  GET /api/admin/ingestion/items/{id}/source.pdf.
//                  False for items uploaded before persistent PDF storage
//                  was added, so the SPA can render a "PDF not retained"
//                  fallback instead of a broken <embed>.
//   Figures      — per-figure spec including a relative URL to the figure
//                  stream endpoint. Empty list when the OCR cascade
//                  extracted no figures or the FigureSpecJson is missing.
//                  The SPA renders these inline alongside the recreated
//                  question text.
//
// OCR-enhancement persistence (ADR-0062 Phase 1.5, added 2026-05-03):
//   EnhancedText — Anthropic-cleaned text persisted on the draft after a
//                  successful POST /enhance-text. Null until the draft
//                  has been enhanced at least once. The SPA renders this
//                  immediately on panel open (no LLM call) when present;
//                  only fires auto-enhance when null.
//   EnhancedAt   — UTC timestamp of the most recent enhance.
//   EnhancedBy   — Anthropic model id ("claude-sonnet-4-6") for the
//                  "Enhanced via LLM (model)" badge.
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
    IReadOnlyList<ExtractedQuestion> ExtractedQuestions,
    bool HasSourcePdf = false,
    IReadOnlyList<ItemFigureRef>? Figures = null,
    string? EnhancedText = null,
    DateTimeOffset? EnhancedAt = null,
    string? EnhancedBy = null,
    // 2026-05-03: surface the auto-classified taxonomy node + metadata
    // state so the SPA can refine the 0-figures warning. The banner
    // text varies by topic — geometry/calculus/vectors/trig nearly
    // always carry diagrams (warning stays loud), but pure algebra /
    // probability / functions can legitimately have none (warning
    // softens to a verify-prompt). Source: PipelineItemDocument.
    // CuratorMetadata?.TaxonomyNode (curator-confirmed) preferred over
    // AutoExtractedMetadata?.TaxonomyNode (heuristic seed); null when
    // neither exists. Format: dotted path like "calculus.derivative_rules".
    string? TaxonomyNode = null,
    // "pending" | "auto_extracted" | "confirmed" — drives whether the
    // SPA shows "we think this is..." vs. "curator confirmed this is...".
    string? MetadataState = null);

public sealed record ItemFigureRef(
    int Index,
    int Page,
    string? Kind,
    string? AltText,
    string Url);

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

// QualityScores surfaces aggregate scores for the pipeline item's
// detail view in the Curator inspector.
//
//   MathCorrectness     — derived from item.AvgQualityScore (ingestion-stage).
//   LanguageQuality     — averaged over QuestionState.LastQualityEvaluation.LanguageQuality
//                         across the item's persisted questions/variants. Null
//                         when no questions exist yet OR none have a quality
//                         evaluation. The SPA hides the bar in the null case.
//   PedagogicalQuality  — same as LanguageQuality but for the pedagogical dim.
//   PlagiarismScore     — derived from item.DuplicateCount (ingestion-stage).
//
// The two LLM-rubric dimensions used to be hardcoded to 80 / 75. That
// shipped a lie — every detail panel showed the same numbers regardless
// of the real evaluation. Made nullable so "we don't have a measurement"
// is expressible end-to-end (per the no-stubs ban, 2026-04-11).
public sealed record QualityScores(
    int MathCorrectness,
    int? LanguageQuality,
    int? PedagogicalQuality,
    int PlagiarismScore);

public sealed record ExtractedQuestion(
    int Index,
    string Text,
    string? Answer,
    float Confidence,
    // 2026-05-03: source page on the original PDF (1-based). Populated
    // for Bagrut drafts (single page per draft) and for variants whose
    // SourceFilename encodes the page (e.g. "math-5u-2026-35581-page3.pdf").
    // Null for items where the page can't be inferred (cloud-dir uploads,
    // legacy items). The SPA uses this to render a thumbnail of the
    // exact PDF page next to each recreated card via #page=N.
    int? SourcePage = null);

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

// Cloud Directory
public sealed record CloudDirListRequest(
    string Provider,        // "s3", "gcs", "azure", "local"
    string BucketOrPath,    // bucket name or local path
    string? Prefix,         // optional prefix/folder path
    string? ContinuationToken);

public sealed record CloudDirListResponse(
    IReadOnlyList<CloudFileEntry> Files,
    int TotalCount,
    string? ContinuationToken);

public sealed record CloudFileEntry(
    string Key,
    string Filename,
    long SizeBytes,
    string ContentType,
    DateTimeOffset LastModified,
    bool AlreadyIngested);

public sealed record CloudDirIngestRequest(
    string Provider,
    string BucketOrPath,
    IReadOnlyList<string> FileKeys,   // specific files to ingest, empty = all
    string? Prefix);

public sealed record CloudDirIngestResponse(
    int FilesQueued,
    int FilesSkipped,
    string BatchId);
