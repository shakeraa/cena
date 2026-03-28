// =============================================================================
// Cena Platform — Ingestion Pipeline Orchestrator
// Coordinates the full pipeline: File → OCR → Segment → Normalize → Classify →
// Dedup → Store. Emits events at each stage for audit and monitoring.
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
    string? ErrorMessage);

public sealed class IngestionOrchestrator : IIngestionOrchestrator
{
    private readonly IOcrClient _ocrClient;
    private readonly IMathOcrClient _mathFallback;
    private readonly IQuestionSegmenter _segmenter;
    private readonly IDeduplicationService _dedup;
    private readonly IDocumentStore _store;
    private readonly INatsConnection _nats;
    private readonly ILogger<IngestionOrchestrator> _logger;

    public IngestionOrchestrator(
        IOcrClient ocrClient,
        IMathOcrClient mathFallback,
        IQuestionSegmenter segmenter,
        IDeduplicationService dedup,
        IDocumentStore store,
        INatsConnection nats,
        ILogger<IngestionOrchestrator> logger)
    {
        _ocrClient = ocrClient;
        _mathFallback = mathFallback;
        _segmenter = segmenter;
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

            // ── Stage 3: Segment questions from each page ──
            AdvanceStage(pipelineItem, PipelineStage.Segmented);

            var allQuestions = new List<SegmentedQuestion>();
            foreach (var page in ocrResult.Pages)
            {
                var pageQuestions = await _segmenter.SegmentAsync(page, ct);
                allQuestions.AddRange(pageQuestions);
            }

            CompleteStage(pipelineItem, PipelineStage.Segmented);

            _logger.LogInformation("Segmented {Count} questions from {File}",
                allQuestions.Count, request.Filename);

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
                    duplicates = duplicateCount
                }), cancellationToken: ct);

            return new IngestionResult(
                PipelineItemId: pipelineItemId,
                QuestionsExtracted: allQuestions.Count,
                QuestionsUnique: questionIds.Count,
                QuestionsDuplicate: duplicateCount,
                AvgQualityScore: pipelineItem.AvgQualityScore ?? 0,
                Success: true,
                ErrorMessage: null);
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
