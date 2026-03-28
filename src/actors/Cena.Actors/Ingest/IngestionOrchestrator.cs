// =============================================================================
// Cena Platform — Ingestion Pipeline Orchestrator
// Coordinates the full pipeline: File -> OCR -> Segment -> Normalize -> Classify ->
// Dedup -> Store. Emits events at each stage for audit and monitoring.
// SAI-07: Content extraction runs in parallel with question segmentation.
//         Content extraction failure does NOT block question pipeline.
// =============================================================================

using System.Security.Cryptography;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Ingest;

public interface IIngestionOrchestrator
{
    /// <summary>Process a single file through the full pipeline.</summary>
    Task<IngestionResult> ProcessFileAsync(IngestionRequest request, CancellationToken ct = default);
}

public sealed record IngestionRequest(
    Stream FileStream,
    string Filename,
    string ContentType,
    string SourceType,          // url, s3, photo, batch
    string? SourceUrl,
    string SubmittedBy);

public sealed record IngestionResult(
    string PipelineItemId,
    int QuestionsExtracted,
    int QuestionsUnique,
    int QuestionsDuplicate,
    float AvgQualityScore,
    bool Success,
    string? ErrorMessage,
    int ContentBlocksExtracted = 0);

public sealed class IngestionOrchestrator : IIngestionOrchestrator
{
    private readonly IOcrClient _ocrClient;
    private readonly IMathOcrClient _mathFallback;
    private readonly IQuestionSegmenter _segmenter;
    private readonly IContentExtractorService _contentExtractor;
    private readonly IDeduplicationService _dedup;
    private readonly IDocumentStore _store;
    private readonly INatsConnection _nats;
    private readonly ILogger<IngestionOrchestrator> _logger;

    public IngestionOrchestrator(
        IOcrClient ocrClient,
        IMathOcrClient mathFallback,
        IQuestionSegmenter segmenter,
        IContentExtractorService contentExtractor,
        IDeduplicationService dedup,
        IDocumentStore store,
        INatsConnection nats,
        ILogger<IngestionOrchestrator> logger)
    {
        _ocrClient = ocrClient;
        _mathFallback = mathFallback;
        _segmenter = segmenter;
        _contentExtractor = contentExtractor;
        _dedup = dedup;
        _store = store;
        _nats = nats;
        _logger = logger;
    }

    public async Task<IngestionResult> ProcessFileAsync(IngestionRequest request, CancellationToken ct = default)
    {
        var pipelineItemId = $"pi-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        try
        {
            // ── Stage 1: Register incoming file ──
            using var ms = new MemoryStream();
            await request.FileStream.CopyToAsync(ms, ct);
            var fileBytes = ms.ToArray();
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
            var s3Key = $"incoming/{now:yyyy/MM/dd}/{contentHash}/{request.Filename}";

            var pipelineItem = new PipelineItemDocument
            {
                Id = pipelineItemId,
                SourceFilename = request.Filename,
                SourceType = request.SourceType,
                SourceUrl = request.SourceUrl,
                S3Key = s3Key,
                ContentHash = contentHash,
                ContentType = request.ContentType,
                FileSizeBytes = fileBytes.Length,
                CurrentStage = PipelineStage.Incoming,
                SubmittedBy = request.SubmittedBy,
                SubmittedAt = now,
                UpdatedAt = now,
                StageHistory = new List<StageRecord>
                {
                    new() { Stage = PipelineStage.Incoming, StartedAt = now, CompletedAt = now, Status = "completed" }
                }
            };

            await using var session = _store.LightweightSession();
            session.Store(pipelineItem);

            // Emit file received event
            var fileReceivedEvent = new FileReceived_V1(
                pipelineItemId, request.Filename, request.SourceType,
                request.SourceUrl, s3Key, contentHash, request.ContentType,
                fileBytes.Length, request.SubmittedBy, now);
            session.Events.Append(pipelineItemId, fileReceivedEvent);

            await _nats.PublishAsync("cena.ingest.file.received",
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { pipelineItemId, request.Filename }), cancellationToken: ct);

            // ── Stage 2: OCR ──
            AdvanceStage(pipelineItem, PipelineStage.OcrProcessing);

            OcrDocumentOutput ocrResult;
            using (var fileStream = new MemoryStream(fileBytes))
            {
                ocrResult = request.ContentType.Contains("pdf")
                    ? await _ocrClient.ProcessDocumentAsync(fileStream, ct)
                    : WrapPageAsDocument(await _ocrClient.ProcessPageAsync(fileStream, request.ContentType, ct));
            }

            pipelineItem.Ocr = new OcrResult
            {
                ModelUsed = _ocrClient.ProviderName,
                FallbackUsed = false,
                Confidence = ocrResult.OverallConfidence,
                DetectedLanguage = ocrResult.DetectedLanguage,
                PageCount = ocrResult.PageCount,
                Pages = ocrResult.Pages.Select(p => new OcrPageResult
                {
                    PageNumber = p.PageNumber,
                    RawText = p.RawText,
                    MathExpressions = p.MathExpressions,
                    Confidence = p.Confidence
                }).ToList(),
                CostUsd = ocrResult.EstimatedCostUsd
            };

            CompleteStage(pipelineItem, PipelineStage.OcrProcessing);

            session.Events.Append(pipelineItemId, new OcrCompleted_V1(
                pipelineItemId, _ocrClient.ProviderName, false,
                ocrResult.OverallConfidence, ocrResult.DetectedLanguage,
                ocrResult.PageCount, ocrResult.EstimatedCostUsd, DateTimeOffset.UtcNow));

            // ── Stage 3: Segment questions + Extract content (parallel) ──
            // SAI-07: Both tasks read from the same OCR output. Content extraction
            // failure does NOT block the question pipeline.
            AdvanceStage(pipelineItem, PipelineStage.Segmented);

            var questionTask = SegmentQuestionsAsync(ocrResult.Pages, ct);
            var contentTask = SafeExtractContentAsync(
                ocrResult.Pages, "math", ocrResult.DetectedLanguage, ct);

            await Task.WhenAll(questionTask, contentTask);

            var allQuestions = questionTask.Result;
            var contentBlocks = contentTask.Result;

            CompleteStage(pipelineItem, PipelineStage.Segmented);

            _logger.LogInformation(
                "Segmented {QuestionCount} questions and {ContentCount} content blocks from {File}",
                allQuestions.Count, contentBlocks.Count, request.Filename);

            // ── Stage 3b: Store content blocks as events + documents ──
            if (contentBlocks.Count > 0)
            {
                AdvanceStage(pipelineItem, PipelineStage.ContentExtraction);

                var contentBlockIds = new List<string>();
                foreach (var block in contentBlocks)
                {
                    var blockId = $"cb-{Guid.NewGuid():N}";
                    contentBlockIds.Add(blockId);

                    var contentEvent = new ContentExtracted_V1(
                        ContentBlockId: blockId,
                        SourceDocId: pipelineItemId,
                        ContentType: block.ContentType,
                        RawText: block.RawText,
                        ProcessedText: block.ProcessedText,
                        ConceptIds: block.ConceptIds,
                        Language: ocrResult.DetectedLanguage,
                        PageRange: block.PageRange,
                        Subject: "math",
                        Topic: block.Topic,
                        Timestamp: DateTimeOffset.UtcNow);

                    session.Events.Append(pipelineItemId, contentEvent);

                    session.Store(new ContentBlockDocument
                    {
                        Id = blockId,
                        SourceDocId = pipelineItemId,
                        ContentType = block.ContentType,
                        RawText = block.RawText,
                        ProcessedText = block.ProcessedText,
                        ConceptIds = block.ConceptIds,
                        Language = ocrResult.DetectedLanguage,
                        PageRange = block.PageRange,
                        Subject = "math",
                        Topic = block.Topic,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                pipelineItem.ExtractedContentBlockIds = contentBlockIds;
                pipelineItem.ExtractedContentBlockCount = contentBlockIds.Count;

                // Aggregate content block type counts and linked concept IDs
                var typeCounts = new Dictionary<string, int>();
                var allLinkedConcepts = new HashSet<string>();
                foreach (var block in contentBlocks)
                {
                    if (!typeCounts.TryAdd(block.ContentType, 1))
                        typeCounts[block.ContentType]++;

                    foreach (var cid in block.ConceptIds)
                    {
                        if (cid != "unlinked")
                            allLinkedConcepts.Add(cid);
                    }
                }
                pipelineItem.ContentBlockTypeCounts = typeCounts;
                pipelineItem.LinkedConceptIds = allLinkedConcepts.ToList();

                CompleteStage(pipelineItem, PipelineStage.ContentExtraction);

                // SAI-06/SAI-07: Publish NATS events for async embedding (non-blocking)
                foreach (var blockId in contentBlockIds)
                {
                    await _nats.PublishAsync("cena.ingest.content.extracted",
                        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                            new { ContentBlockId = blockId, PipelineItemId = pipelineItemId }),
                        cancellationToken: ct);
                }
            }

            // ── Stage 4: Normalize + Dedup each question ──
            AdvanceStage(pipelineItem, PipelineStage.Normalized);

            var questionIds = new List<string>();
            int duplicateCount = 0;
            float totalQuality = 0;

            foreach (var q in allQuestions)
            {
                // Check dedup before creating event
                var dedupResult = await _dedup.CheckAsync(q.StemText, q.MathExpressions, ct);

                if (dedupResult.Result == DedupResult.ExactDuplicate)
                {
                    duplicateCount++;
                    continue;
                }

                var questionId = $"q-{Guid.NewGuid():N}";
                questionIds.Add(questionId);

                // Create question event
                var ingestedEvent = new QuestionIngested_V1(
                    QuestionId: questionId,
                    Stem: q.StemText,
                    StemHtml: $"<p>{q.StemText}</p>",
                    Options: new List<QuestionOptionData>(),   // Open-ended Bagrut — no MCQ options
                    Subject: "math",
                    Topic: "",                                  // Classified in next stage
                    Grade: "5 Units",
                    BloomsLevel: 3,                            // Default, classified in next stage
                    Difficulty: 0.5f,                          // Default, classified in next stage
                    ConceptIds: new List<string>(),
                    Language: ocrResult.DetectedLanguage,
                    SourceDocId: pipelineItemId,
                    SourceUrl: request.SourceUrl ?? "",
                    SourceFilename: request.Filename,
                    OriginalText: q.StemText,
                    ImportedBy: request.SubmittedBy,
                    Timestamp: DateTimeOffset.UtcNow);

                session.Events.StartStream<QuestionState>(questionId, ingestedEvent);

                // Register in dedup index
                await _dedup.RegisterAsync(questionId, dedupResult.ExactHash, dedupResult.StructuralHash, ct);

                // Track quality (basic — full classification in separate service)
                totalQuality += q.Confidence;

                if (dedupResult.Result == DedupResult.StructuralDuplicate ||
                    dedupResult.Result == DedupResult.SemanticNearDuplicate)
                {
                    duplicateCount++;
                }
            }

            CompleteStage(pipelineItem, PipelineStage.Normalized);

            // ── Update pipeline item with results ──
            pipelineItem.ExtractedQuestionIds = questionIds;
            pipelineItem.ExtractedQuestionCount = allQuestions.Count;
            pipelineItem.DuplicateCount = duplicateCount;
            pipelineItem.AvgQualityScore = questionIds.Count > 0 ? totalQuality / questionIds.Count : 0;
            pipelineItem.CurrentStage = PipelineStage.Classified;
            pipelineItem.Status = "completed";
            pipelineItem.CompletedAt = DateTimeOffset.UtcNow;
            pipelineItem.UpdatedAt = DateTimeOffset.UtcNow;

            session.Store(pipelineItem);
            await session.SaveChangesAsync(ct);

            await _nats.PublishAsync("cena.ingest.item.classified",
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                {
                    pipelineItemId,
                    questionsExtracted = allQuestions.Count,
                    questionsUnique = questionIds.Count,
                    duplicates = duplicateCount,
                    contentBlocksExtracted = contentBlocks.Count
                }), cancellationToken: ct);

            return new IngestionResult(
                PipelineItemId: pipelineItemId,
                QuestionsExtracted: allQuestions.Count,
                QuestionsUnique: questionIds.Count,
                QuestionsDuplicate: duplicateCount,
                AvgQualityScore: pipelineItem.AvgQualityScore ?? 0,
                Success: true,
                ErrorMessage: null,
                ContentBlocksExtracted: contentBlocks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion pipeline failed for {Filename}", request.Filename);

            // Record failure
            await using var failSession = _store.LightweightSession();
            failSession.Events.Append(pipelineItemId, new PipelineStageFailed_V1(
                pipelineItemId, PipelineStage.Failed, ex.Message, 0, DateTimeOffset.UtcNow));
            await failSession.SaveChangesAsync(ct);

            return new IngestionResult(pipelineItemId, 0, 0, 0, 0, false, ex.Message);
        }
    }

    /// <summary>
    /// SAI-07: Fault-tolerant content extraction wrapper.
    /// Content extraction failure is logged but does NOT propagate — returns empty list.
    /// This ensures the question pipeline continues even if content extraction fails.
    /// </summary>
    private async Task<IReadOnlyList<ExtractedContentBlock>> SafeExtractContentAsync(
        List<OcrPageOutput> pages, string subject, string language, CancellationToken ct)
    {
        try
        {
            return await _contentExtractor.ExtractAsync(pages, subject, language, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Content extraction failed for {PageCount} pages, continuing with question pipeline only",
                pages.Count);
            return [];
        }
    }

    private async Task<List<SegmentedQuestion>> SegmentQuestionsAsync(
        List<OcrPageOutput> pages, CancellationToken ct)
    {
        var allQuestions = new List<SegmentedQuestion>();
        foreach (var page in pages)
        {
            var pageQuestions = await _segmenter.SegmentAsync(page, ct);
            allQuestions.AddRange(pageQuestions);
        }
        return allQuestions;
    }

    private static void AdvanceStage(PipelineItemDocument item, PipelineStage stage)
    {
        item.CurrentStage = stage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.StageHistory.Add(new StageRecord
        {
            Stage = stage,
            StartedAt = DateTimeOffset.UtcNow,
            Status = "processing"
        });
    }

    private static void CompleteStage(PipelineItemDocument item, PipelineStage stage)
    {
        var record = item.StageHistory.LastOrDefault(s => s.Stage == stage);
        if (record is not null)
        {
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.Status = "completed";
        }
    }

    private static OcrDocumentOutput WrapPageAsDocument(OcrPageOutput page) =>
        new(new List<OcrPageOutput> { page }, page.DetectedLanguage, page.Confidence, 1, 0.0003m);
}
