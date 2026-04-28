// =============================================================================
// Cena Platform — BagrutDraftPersistence
//
// Bridges the gap that BagrutPdfIngestionService leaves: it returns
// IngestionDraftQuestion[] in the response and otherwise discards them.
// This service projects each draft into a PipelineItemDocument so curators
// can find them on the existing Ingestion Pipeline kanban (In Review
// column, Source type = "bagrut").
//
// Rationale: reuse the curator review UI that already exists for
// cloud-dir items rather than building a parallel "Bagrut drafts" page.
// Drafts land in InReview because OCR + segmentation already happened
// during the cascade — there's nothing left to do upstream of curator
// review.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion;

public interface IBagrutDraftPersistence
{
    Task<IReadOnlyList<string>> PersistAsync(
        string examCode,
        string sourcePdfId,
        string? sourceFilename,
        string submittedBy,
        IReadOnlyList<IngestionDraftQuestion> drafts,
        CancellationToken ct = default);
}

public sealed class BagrutDraftPersistence : IBagrutDraftPersistence
{
    private readonly IDocumentStore _store;
    private readonly ILogger<BagrutDraftPersistence> _logger;

    public BagrutDraftPersistence(
        IDocumentStore store,
        ILogger<BagrutDraftPersistence> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> PersistAsync(
        string examCode,
        string sourcePdfId,
        string? sourceFilename,
        string submittedBy,
        IReadOnlyList<IngestionDraftQuestion> drafts,
        CancellationToken ct = default)
    {
        if (drafts.Count == 0) return Array.Empty<string>();

        var now = DateTimeOffset.UtcNow;
        var ids = new List<string>(drafts.Count);

        await using var session = _store.LightweightSession();
        foreach (var d in drafts)
        {
            // Synthesise a stable per-draft id derived from the draftId
            // emitted by BagrutPdfIngestionService.ExtractQuestions. Using
            // the draftId verbatim keeps re-uploads idempotent: same draft
            // id → upserts the same row instead of duplicating.
            var id = d.DraftId;
            ids.Add(id);

            var stageRecord = new StageRecord
            {
                Stage = Cena.Actors.Ingest.PipelineStage.InReview,
                StartedAt = now,
                Status = "processing",
            };

            var doc = new PipelineItemDocument
            {
                Id = id,
                SourceFilename = sourceFilename ?? $"{examCode}-page{d.SourcePage}.pdf",
                SourceType = "bagrut",
                SourceUrl = sourcePdfId, // pdfId for cross-link
                ContentType = "application/pdf",
                ContentHash = id, // already SHA-derived in ExtractQuestions
                CurrentStage = Cena.Actors.Ingest.PipelineStage.InReview,
                Status = "processing",
                StageHistory = new List<StageRecord> { stageRecord },
                ExtractedQuestionCount = 1,
                AvgQualityScore = (float)d.ExtractionConfidence,
                SubmittedBy = submittedBy,
                SubmittedAt = now,
                UpdatedAt = now,
                MetadataState = "auto_extracted",
            };

            session.Store(doc);

            // Sibling payload row — actual prompt+LaTeX content keyed by
            // the same id, read by GenerateVariantsJobStrategy when the
            // curator clicks "Generate variants" on the kanban card.
            session.Store(new BagrutDraftPayloadDocument
            {
                Id = id,
                ExamCode = examCode,
                SourcePdfId = sourcePdfId,
                SourcePage = d.SourcePage,
                Prompt = d.Prompt,
                LatexContent = d.LatexContent,
                FigureSpecJson = d.FigureSpecJson,
                ExtractionConfidence = d.ExtractionConfidence,
                ReviewNotes = d.ReviewNotes.ToList(),
                CreatedAt = now,
            });
        }

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Persisted {Count} Bagrut drafts to PipelineItemDocument + BagrutDraftPayloadDocument (examCode={ExamCode}, pdfId={PdfId})",
            drafts.Count, examCode, sourcePdfId);

        return ids;
    }
}
