// =============================================================================
// Cena Platform -- Ingestion Pipeline Service
// ADM-015: Pipeline dashboard and management (production-grade)
// All methods read real Marten documents. No stubs, no Random, no literals.
// =============================================================================

using System.Security.Cryptography;
using Cena.Actors.Ingest;
using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api;

public interface IIngestionPipelineService
{
    Task<PipelineStatusResponse> GetPipelineStatusAsync();
    Task<PipelineItemDetailResponse?> GetItemDetailAsync(string id);
    Task<bool> RetryItemAsync(string id);
    Task<bool> RejectPipelineItemAsync(string id, string reason);
    Task<UploadFileResponse> UploadFileAsync(string filename, string contentType);
    Task<bool> MoveToReviewAsync(string id);
    /// <summary>
    /// Curator approves an InReview item for publication and transitions
    /// it to <see cref="Cena.Actors.Ingest.PipelineStage.Published"/>.
    /// Returns <c>(false, "&lt;reason&gt;")</c> when the item cannot advance
    /// — typically because metadata is unconfirmed or the item is not
    /// currently in InReview. <c>(true, null)</c> on success.
    /// </summary>
    Task<(bool Success, string? Reason)> ApproveAsync(string id, string approvedBy);
    Task<UploadFileResponse> UploadFromRequestAsync(HttpRequest request);
    Task<PipelineStatsResponse> GetStatsAsync();
    Task<CloudDirListResponse> ListCloudDirectoryAsync(CloudDirListRequest request);
    Task<CloudDirIngestResponse> IngestCloudDirectoryAsync(CloudDirIngestRequest request);
}

public sealed partial class IngestionPipelineService : IIngestionPipelineService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly IIngestionOrchestrator? _orchestrator;
    private readonly Cena.Admin.Api.Ingestion.ICuratorMetadataService? _metadataService;
    private readonly ILogger<IngestionPipelineService> _logger;
    // ADR-0058: cloud-directory dispatch moved behind a provider
    // abstraction. Path-traversal allowlist + SHA-256 dedup + S3 SDK
    // calls all live in the per-provider implementations now.
    private readonly ICloudDirectoryProviderRegistry _cloudDirRegistry;

    // Visual-review (2026-05-01): nullable so the existing host
    // composition that doesn't pass a store stays buildable. When null,
    // hasSourcePdf falls back to the SourcePdfId-string check (legacy
    // behaviour) — which is fine for unit tests but lies to the SPA
    // when bytes were never persisted (the embed renders, then 404s).
    private readonly Cena.Admin.Api.Ingestion.IBagrutPdfStore? _pdfStore;

    public IngestionPipelineService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<IngestionPipelineService> logger,
        ICloudDirectoryProviderRegistry cloudDirRegistry,
        IIngestionOrchestrator? orchestrator = null,
        Cena.Admin.Api.Ingestion.ICuratorMetadataService? metadataService = null,
        Cena.Admin.Api.Ingestion.IBagrutPdfStore? pdfStore = null)
    {
        _store = store;
        _redis = redis;
        _orchestrator = orchestrator;
        _metadataService = metadataService;
        _logger = logger;
        _cloudDirRegistry = cloudDirRegistry;
        _pdfStore = pdfStore;
    }

    public async Task<PipelineStatusResponse> GetPipelineStatusAsync()
    {
        await using var session = _store.QuerySession();

        // Query real pipeline items grouped by stage
        var allItems = await session.Query<PipelineItemDocument>()
            .OrderByDescending(x => x.SubmittedAt)
            .Take(500)
            .ToListAsync();

        var stageNames = new[]
        {
            (Cena.Actors.Ingest.PipelineStage.Incoming, "incoming", "Incoming"),
            (Cena.Actors.Ingest.PipelineStage.OcrProcessing, "ocr", "OCR Processing"),
            (Cena.Actors.Ingest.PipelineStage.Segmented, "segmented", "Segmented"),
            (Cena.Actors.Ingest.PipelineStage.Normalized, "normalized", "Normalized"),
            (Cena.Actors.Ingest.PipelineStage.Classified, "classified", "Classified"),
            (Cena.Actors.Ingest.PipelineStage.Deduplicated, "deduplicated", "Deduplicated"),
            (Cena.Actors.Ingest.PipelineStage.ReCreated, "recreated", "Re-Created"),
            (Cena.Actors.Ingest.PipelineStage.InReview, "review", "In Review"),
            (Cena.Actors.Ingest.PipelineStage.Published, "published", "Published")
        };

        var stages = stageNames.Select(s =>
        {
            var stageItems = allItems.Where(i => i.CurrentStage == s.Item1).ToList();
            var hasError = stageItems.Any(i => i.Status == "failed");
            return new IngestionDto.PipelineStage(
                StageId: s.Item2,
                Name: s.Item3,
                Count: stageItems.Count,
                Status: hasError ? "failed" : stageItems.Count > 50 ? "slow" : "healthy",
                Items: stageItems.Take(10).Select(i => new IngestionDto.PipelineItem(
                    Id: i.Id,
                    SourceFilename: i.SourceFilename,
                    SourceType: i.SourceType,
                    QuestionCount: i.ExtractedQuestionCount,
                    QualityScore: (int)((i.AvgQualityScore ?? 0) * 100),
                    Timestamp: i.SubmittedAt,
                    ErrorMessage: i.LastError,
                    HasError: i.Status == "failed"
                )).ToList());
        }).ToList();

        return new PipelineStatusResponse(DateTimeOffset.UtcNow, stages);
    }

    public async Task<PipelineItemDetailResponse?> GetItemDetailAsync(string id)
    {
        await using var session = _store.QuerySession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return null;

        var stageHistory = item.StageHistory.Select(s => new StageProcessingInfo(
            Stage: s.Stage.ToString(),
            StartedAt: s.StartedAt,
            CompletedAt: s.CompletedAt,
            Duration: s.Duration,
            Status: s.Status,
            ErrorMessage: s.ErrorMessage
        )).ToList();

        OcrOutput? ocrOutput = null;
        if (item.Ocr is not null)
        {
            // ADR-0058: S3-ingested items carry their own bucket; fall
            // back to the legacy "cena-ingest" placeholder for rows
            // that predate the S3Bucket column.
            var bucket = !string.IsNullOrEmpty(item.S3Bucket) ? item.S3Bucket : "cena-ingest";
            ocrOutput = new OcrOutput(
                OriginalImageUrl: $"s3://{bucket}/{item.S3Key}",
                ExtractedText: item.Ocr.Pages.FirstOrDefault()?.RawText ?? "",
                Confidence: item.Ocr.Confidence,
                Regions: new List<OcrRegion>());
        }

        var extractedQuestions = new List<ExtractedQuestion>();
        int? languageQualityAvg    = null;
        int? pedagogicalQualityAvg = null;
        if (item.ExtractedQuestionIds.Count > 0)
        {
            var questions = await session.Query<Cena.Actors.Questions.QuestionReadModel>()
                .Where(q => q.Id.IsOneOf(item.ExtractedQuestionIds.ToArray()))
                .ToListAsync();

            // Per-question source page lives on QuestionState.Provenance.SourceFilename
            // (event-sourced; not on QuestionReadModel). Same fan-out as
            // the quality-score block below, but populated FIRST so the
            // ExtractedQuestion ctor can include SourcePage on each row.
            var sourcePageByQid = new Dictionary<string, int?>(StringComparer.Ordinal);
            // Per-dimension scores live on QuestionState.LastQualityEvaluation
            // (event-sourced). QuestionReadModel only carries the composite
            // QualityScore. Per-call cost: N stream rehydrations on detail-
            // panel open (typically 5-20). Acceptable for a curator-action
            // endpoint. Promote into QuestionListProjection if the cost bites.
            var langScores = new List<int>();
            var pedScores  = new List<int>();
            foreach (var qid in item.ExtractedQuestionIds)
            {
                var qstate = await session.Events
                    .AggregateStreamAsync<Cena.Actors.Questions.QuestionState>(qid);
                if (qstate?.Provenance?.SourceFilename is { Length: > 0 } fn)
                    sourcePageByQid[qid] = ExtractPageFromFilename(fn);
                var eval = qstate?.LastQualityEvaluation;
                if (eval is null) continue;
                langScores.Add(eval.LanguageQuality);
                pedScores.Add(eval.PedagogicalQuality);
            }

            extractedQuestions = questions.Select((q, i) => new ExtractedQuestion(
                Index: i,
                Text: q.StemPreview,
                Answer: null,
                Confidence: q.QualityScore / 100f,
                SourcePage: sourcePageByQid.TryGetValue(q.Id, out var p) ? p : null
            )).ToList();

            // Round-half-away-from-zero so a 79.5 average doesn't display
            // as "79" via banker's rounding — curators read these as
            // grades and the small visual difference matters.
            if (langScores.Count > 0)
                languageQualityAvg = (int)Math.Round(langScores.Average(), MidpointRounding.AwayFromZero);
            if (pedScores.Count > 0)
                pedagogicalQualityAvg = (int)Math.Round(pedScores.Average(), MidpointRounding.AwayFromZero);
        }

        // Visual-review (2026-05-01): surface source-PDF availability +
        // figure list so the SPA can render the side-by-side viewer.
        // Bagrut items have a sibling BagrutDraftPayloadDocument; non-Bagrut
        // (cloud-dir, photo-upload) items don't, so HasSourcePdf is false
        // and Figures is empty for those — the curator panel falls back to
        // text-only review.
        //
        // hasSourcePdf semantics: "the GET /source.pdf endpoint will return
        // bytes". A SourcePdfId string is NOT enough — items uploaded
        // before the PDF store landed have a SourcePdfId reference but no
        // bytes on disk, and the SPA's <embed> would render then 404
        // ("localhost refused to connect" in the browser). Probe the store
        // for actual existence so the SPA can correctly fall back to the
        // "PDF not retained — re-upload" placeholder. _pdfStore is
        // nullable for tests that don't wire it.
        bool hasSourcePdf = false;
        IReadOnlyList<ItemFigureRef>? figures = null;
        // ADR-0062 Phase 1.5 — enhanced-text persistence. Surfaced on the
        // detail response so the SPA renders the cleaned view on panel
        // open without a fresh /enhance-text round-trip.
        string? enhancedText = null;
        DateTimeOffset? enhancedAt = null;
        string? enhancedBy = null;
        if (string.Equals(item.SourceType, "bagrut", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await session.LoadAsync<BagrutDraftPayloadDocument>(item.Id);
            if (payload is not null)
            {
                enhancedText = payload.EnhancedText;
                enhancedAt = payload.EnhancedAt;
                enhancedBy = payload.EnhancedBy;
                if (!string.IsNullOrWhiteSpace(payload.SourcePdfId))
                {
                    hasSourcePdf = _pdfStore is null
                        ? false   // no store wired → conservatively claim "no" so the SPA hides the embed
                        : await _pdfStore.ExistsAsync(payload.SourcePdfId);
                }
                figures = ParseFigureSpec(payload.FigureSpecJson, item.Id);

                // Surface the draft prompt as the "extracted question" so
                // the curator panel has content to review BEFORE variants
                // are generated. For Bagrut drafts, item.ExtractedQuestionIds
                // is empty by design (variants are spawned later), so
                // without this fallback the panel renders "No question
                // content yet — pipeline is still processing" indefinitely
                // (2026-05-01 user report). The prompt + LaTeX combo is
                // exactly what BagrutPdfIngestionService put in the payload
                // — same content the AI variant generator will seed from.
                if (extractedQuestions.Count == 0)
                {
                    var combined = string.IsNullOrWhiteSpace(payload.LatexContent)
                        ? payload.Prompt
                        : $"{payload.Prompt}\n\n{payload.LatexContent}";
                    if (!string.IsNullOrWhiteSpace(combined))
                    {
                        extractedQuestions = new List<ExtractedQuestion>
                        {
                            new ExtractedQuestion(
                                Index: 0,
                                Text: combined,
                                Answer: null,
                                Confidence: (float)payload.ExtractionConfidence,
                                SourcePage: payload.SourcePage > 0 ? payload.SourcePage : null)
                        };
                    }
                }
            }
        }

        // 2026-05-03: TaxonomyNode for the 0-figures banner refinement.
        // Curator-confirmed wins over auto-extracted (the curator
        // trumps the heuristic), and a null TaxonomyNode is meaningful
        // — the SPA falls back to the generic "may be missing diagrams"
        // copy when classification couldn't bucket the item. We do NOT
        // synthesise a taxonomy here; null is honest.
        var taxonomyNode  = item.CuratorMetadata?.TaxonomyNode
                            ?? item.AutoExtractedMetadata?.TaxonomyNode;
        var metadataState = item.MetadataState;

        return new PipelineItemDetailResponse(
            Id: item.Id,
            SourceFilename: item.SourceFilename,
            SourceType: item.SourceType,
            SourceUrl: item.SourceUrl ?? item.S3Key,
            SubmittedAt: item.SubmittedAt,
            CompletedAt: item.CompletedAt,
            CurrentStage: item.CurrentStage.ToString(),
            StageHistory: stageHistory,
            OcrResult: ocrOutput,
            Quality: new QualityScores(
                MathCorrectness: (int)((item.AvgQualityScore ?? 0) * 100),
                LanguageQuality: languageQualityAvg,
                PedagogicalQuality: pedagogicalQualityAvg,
                PlagiarismScore: item.DuplicateCount ?? 0),
            ExtractedQuestions: extractedQuestions,
            HasSourcePdf: hasSourcePdf,
            Figures: figures,
            EnhancedText: enhancedText,
            EnhancedAt: enhancedAt,
            EnhancedBy: enhancedBy,
            TaxonomyNode: taxonomyNode,
            MetadataState: metadataState);
    }

    /// <summary>
    /// Pull the 1-based page number out of a Bagrut variant filename.
    /// Variants persisted by GenerateVariantsJobStrategy.BuildVariantCreateRequest
    /// use the convention "{examCode}-page{N}.pdf" — e.g.
    /// "math-5u-2026-35581-page3.pdf" → 3. Returns null when the
    /// pattern doesn't match (legacy items, non-Bagrut sources).
    /// </summary>
    internal static int? ExtractPageFromFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            filename, @"-page(?<n>\d+)\.pdf$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return int.TryParse(m.Groups["n"].Value, out var n) && n > 0 ? n : null;
    }

    /// <summary>
    /// Parse the Bagrut FigureSpecJson written by BagrutPdfIngestionService
    /// into a list of ItemFigureRef the SPA can iterate. The URL points at
    /// the figure stream endpoint indexed by position in the original spec.
    /// Returns an empty list (not null) when the spec is missing or
    /// malformed so the SPA can branch on `figures.length === 0` cleanly.
    /// </summary>
    private static IReadOnlyList<ItemFigureRef> ParseFigureSpec(string? json, string itemId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ItemFigureRef>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("figures", out var arr)
                || arr.ValueKind != System.Text.Json.JsonValueKind.Array)
                return Array.Empty<ItemFigureRef>();

            var refs = new List<ItemFigureRef>(arr.GetArrayLength());
            int i = 0;
            foreach (var f in arr.EnumerateArray())
            {
                int page = f.TryGetProperty("page", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
                string? kind = f.TryGetProperty("kind", out var k) ? k.GetString() : null;
                string? alt  = f.TryGetProperty("altText", out var a) ? a.GetString() : null;
                refs.Add(new ItemFigureRef(
                    Index: i,
                    Page: page,
                    Kind: kind,
                    AltText: alt,
                    Url: $"/api/admin/ingestion/items/{itemId}/figures/{i}"));
                i++;
            }
            return refs;
        }
        catch (System.Text.Json.JsonException)
        {
            // FigureSpecJson is server-generated; a malformed value is a
            // bug we'd want to see in logs, but it must not crash the
            // detail panel. Return empty list and move on.
            return Array.Empty<ItemFigureRef>();
        }
    }

    public async Task<bool> RetryItemAsync(string id)
    {
        // PRR-RETRY-IMPL: reset the doc to a retriable state. The actual
        // re-processing happens out-of-band on the IngestionRetryWorker
        // (Actor Host BackgroundService) which scans for items where
        // Status=processing+CurrentStage=Incoming+RetryCount>0 past their
        // backoff window, fetches the persisted bytes via the configured
        // IIngestionBytesStore, and re-invokes ProcessFileAsync.
        //
        // Legacy items (BytesPersisted=false) cannot be retried — the
        // bytes were never durably persisted. We refuse here with a
        // distinct error so the SPA can render "please re-upload" rather
        // than a generic failure. The doc is NOT mutated in that case.
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return false;

        if (!item.BytesPersisted)
        {
            _logger.LogWarning(
                "RetryItemAsync refused for {ItemId}: BytesPersisted=false (legacy item, " +
                "uploaded before PRR-RETRY-IMPL). Curator must re-upload via /api/admin/ingestion/upload.",
                id);
            throw new InvalidOperationException(
                "BYTES_NOT_PERSISTED: this pipeline item was uploaded before bytes-persistence " +
                "was wired (or the bytes-store PUT failed at upload time). The original file is " +
                "not retrievable, so retry cannot run. Please re-upload via " +
                "POST /api/admin/ingestion/upload.");
        }

        item.RetryCount++;
        item.Status = "processing";
        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.Incoming;
        item.LastError = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(item);
        await session.SaveChangesAsync();

        _logger.LogInformation(
            "RetryItemAsync: reset {ItemId} to retriable (attempt {Attempt}); " +
            "IngestionRetryWorker will pick it up after backoff.",
            id, item.RetryCount);
        return true;
    }

    public async Task<bool> RejectPipelineItemAsync(string id, string reason)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return false;

        item.Status = "rejected";
        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.Failed;
        item.LastError = reason;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(item);
        await session.SaveChangesAsync();

        _logger.LogInformation("Rejected pipeline item {ItemId}: {Reason}", id, reason);
        return true;
    }

    public async Task<bool> MoveToReviewAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return false;

        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.InReview;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(item);

        session.Events.Append(id, new MovedToReview_V1(
            id, item.ExtractedQuestionCount, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        _logger.LogInformation("Moved pipeline item {ItemId} to review", id);
        return true;
    }

    public async Task<(bool Success, string? Reason)> ApproveAsync(string id, string approvedBy)
    {
        if (string.IsNullOrWhiteSpace(approvedBy))
            return (false, "approved_by_required");

        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(id);
        if (item is null) return (false, "not_found");

        // Gate 1: stage must be InReview. Approving a still-processing item
        // would skip stage validation; approving a Published item is a
        // no-op the SPA shouldn't have offered. Reject loudly so the
        // operator notices a state-machine drift.
        if (item.CurrentStage != Cena.Actors.Ingest.PipelineStage.InReview)
        {
            _logger.LogWarning(
                "ApproveAsync rejected: item {ItemId} is at stage {Stage}, expected InReview",
                id, item.CurrentStage);
            return (false, $"wrong_stage:{item.CurrentStage}");
        }

        // Gate 2: curator metadata must be confirmed. Without confirmation,
        // there's no validated Subject/Language/SourceType — publishing
        // would push unverified content downstream. RDY-019e §4 ties the
        // stage transition to the metadata handshake.
        if (!string.Equals(item.MetadataState, "confirmed", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "ApproveAsync rejected: item {ItemId} metadata state is {State}, expected confirmed",
                id, item.MetadataState);
            return (false, $"metadata_unconfirmed:{item.MetadataState}");
        }

        var now = DateTimeOffset.UtcNow;

        // Close the InReview stage record if open, then append a Published
        // stage record so the StageHistory timeline reflects the transition
        // in the same shape every other transition uses.
        var openInReview = item.StageHistory.LastOrDefault(s =>
            s.Stage == Cena.Actors.Ingest.PipelineStage.InReview && s.CompletedAt is null);
        if (openInReview is not null)
        {
            openInReview.CompletedAt = now;
            openInReview.Status = "completed";
        }

        item.StageHistory.Add(new StageRecord
        {
            Stage = Cena.Actors.Ingest.PipelineStage.Published,
            StartedAt = now,
            CompletedAt = now,
            Status = "completed",
        });

        item.CurrentStage = Cena.Actors.Ingest.PipelineStage.Published;
        item.Status = "published";
        item.CompletedAt = now;
        item.UpdatedAt = now;
        session.Store(item);

        session.Events.Append(id, new PipelineItemApproved_V1(
            id, approvedBy, item.ExtractedQuestionCount, now));
        await session.SaveChangesAsync();

        _logger.LogInformation(
            "Approved pipeline item {ItemId} for publication (by {ApprovedBy}, {QuestionCount} questions)",
            id, approvedBy, item.ExtractedQuestionCount);
        return (true, null);
    }

    public async Task<UploadFileResponse> UploadFromRequestAsync(HttpRequest request)
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null)
            return new IngestionDto.UploadFileResponse("", "error", null);

        if (_orchestrator is null)
            return new IngestionDto.UploadFileResponse("", "error", null);

        // Read into buffer so we can hand the same bytes to (a) the
        // orchestrator pipeline and (b) the RDY-019e CuratorMetadata
        // auto-extractor. Orchestrator copies to its own MemoryStream so
        // we're safe passing the underlying array.
        using var buffer = new MemoryStream();
        await using (var upstream = file.OpenReadStream())
        {
            await upstream.CopyToAsync(buffer);
        }
        var bytes = buffer.ToArray();
        buffer.Position = 0;

        var contentType = file.ContentType ?? "application/octet-stream";
        var result = await _orchestrator.ProcessFileAsync(new IngestionRequest(
            FileStream: buffer,
            Filename: file.FileName,
            ContentType: contentType,
            SourceType: "upload",
            SourceUrl: null,
            SubmittedBy: "admin"
        ));

        // RDY-019e-IMPL: run the CuratorMetadata auto-extractor on every
        // successful upload. Failures are swallowed with a warning —
        // auto-extract never blocks the upload path. If the service is
        // not wired (legacy configuration), skip silently.
        if (_metadataService is not null && !string.IsNullOrEmpty(result.PipelineItemId))
        {
            try
            {
                await _metadataService.AutoExtractAsync(
                    itemId: result.PipelineItemId,
                    filename: file.FileName,
                    fileBytes: bytes,
                    contentType: contentType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CuratorMetadata auto-extract failed for pipeline item {ItemId}; curator will fill manually.",
                    result.PipelineItemId);
            }
        }

        return new IngestionDto.UploadFileResponse(
            UploadId: result.PipelineItemId,
            Status: result.Success ? "completed" : "failed",
            PipelineItemId: result.PipelineItemId);
    }

    public async Task<UploadFileResponse> UploadFileAsync(string filename, string contentType)
    {
        // Creates a placeholder PipelineItemDocument row so the dashboard can
        // track the queued upload. Actual file bytes arrive through the
        // UploadFromRequestAsync path; this method is used by the admin UI to
        // pre-register an upload slot and return a real id the frontend can poll.
        var uploadId = $"upload-{Guid.NewGuid():N}";
        await using var session = _store.LightweightSession();
        var doc = new PipelineItemDocument
        {
            Id = uploadId,
            SourceFilename = filename,
            SourceType = "upload",
            ContentType = contentType,
            Status = "queued",
            CurrentStage = Cena.Actors.Ingest.PipelineStage.Incoming,
            SubmittedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SubmittedBy = "admin",
        };
        session.Store(doc);
        await session.SaveChangesAsync();
        return new IngestionDto.UploadFileResponse(uploadId, "queued", uploadId);
    }

    public async Task<PipelineStatsResponse> GetStatsAsync()
    {
        await using var session = _store.QuerySession();

        var allItems = await session.Query<PipelineItemDocument>()
            .Where(x => x.SubmittedAt >= DateTimeOffset.UtcNow.AddDays(-7))
            .ToListAsync();

        // Throughput: items processed per hour (last 24h)
        var throughput = Enumerable.Range(0, 24)
            .Select(i =>
            {
                var hour = DateTimeOffset.UtcNow.AddHours(-23 + i);
                var count = allItems.Count(x =>
                    x.CompletedAt.HasValue &&
                    x.CompletedAt.Value.Hour == hour.Hour &&
                    x.CompletedAt.Value.Date == hour.Date);
                return new ThroughputPoint(hour.ToString("yyyy-MM-dd HH:00"), count);
            }).ToList();

        // Failure rates per stage
        var stageNames = new[] { "Incoming", "OCR Processing", "Segmented", "Normalized", "Classified", "Deduplicated", "Re-Created" };
        var failureRates = stageNames.Select(s =>
        {
            var stageItems = allItems.Where(x => x.StageHistory.Any(h => h.Stage.ToString() == s)).ToList();
            var failed = stageItems.Count(x => x.Status == "failed");
            var total = Math.Max(stageItems.Count, 1);
            return new StageFailureRate(s, (float)failed / total, total, failed);
        }).ToList();

        // Processing times per stage
        var processingTimes = stageNames.Select(s =>
        {
            var durations = allItems
                .SelectMany(x => x.StageHistory)
                .Where(h => h.Stage.ToString() == s && h.Duration.HasValue)
                .Select(h => (float)h.Duration!.Value.TotalSeconds)
                .ToList();

            var avg = durations.Count > 0 ? durations.Average() : 0f;
            var p95 = durations.Count > 0 ? durations.OrderBy(d => d).ElementAt((int)(durations.Count * 0.95)) : 0f;
            return new AvgProcessingTime(s, avg, p95);
        }).ToList();

        // Queue depth trend
        var queueTrend = new QueueDepthTrend(
            Enumerable.Range(0, 24)
                .Select(i =>
                {
                    var hour = DateTimeOffset.UtcNow.AddHours(-23 + i);
                    var incoming = allItems.Count(x => x.SubmittedAt.Hour == hour.Hour && x.SubmittedAt.Date == hour.Date);
                    var processing = allItems.Count(x =>
                        x.Status == "processing" &&
                        x.UpdatedAt.Hour == hour.Hour);
                    var completed = allItems.Count(x =>
                        x.CompletedAt.HasValue &&
                        x.CompletedAt.Value.Hour == hour.Hour &&
                        x.CompletedAt.Value.Date == hour.Date);
                    return new DepthPoint(hour.ToString("yyyy-MM-dd HH:00"), incoming, processing, completed);
                }).ToList());

        return new PipelineStatsResponse(throughput, failureRates, processingTimes, queueTrend);
    }

}
