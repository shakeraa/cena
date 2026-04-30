// =============================================================================
// Cena Platform — IngestionJobBagrutStrategy (extracted from IngestionJobRunnerHostedService for the LOC ratchet).
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Ingestion;


internal sealed class BagrutIngestionJobStrategy : IIngestionJobStrategy
{
    public IngestionJobType Type => IngestionJobType.Bagrut;

    public async Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<BagrutJobPayload>(job.PayloadJson ?? "{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Bagrut job payload missing");

        await progress.ReportAsync(5, "Reading PDF…", ct);

        var service = scoped.GetRequiredService<IBagrutPdfIngestionService>();
        var persistence = scoped.GetRequiredService<IBagrutDraftPersistence>();

        await progress.ReportAsync(15, "Running OCR cascade…", ct);

        var result = await service.IngestAsync(
            payload.FileBytes, payload.ExamCode, payload.UploadedBy, ct);

        await progress.ReportAsync(80, $"Persisting {result.Drafts.Count} drafts…", ct);

        var ids = await persistence.PersistAsync(
            examCode: payload.ExamCode,
            sourcePdfId: result.PdfId,
            sourceFilename: payload.SourceFilename,
            submittedBy: payload.UploadedBy,
            drafts: result.Drafts,
            isMinistryReference: payload.IsMinistryReference,
            ct: ct);

        // ADR-0043 §runtime-gate: write BagrutCorpusItemDocument rows so the
        // similarity rejector has Ministry text to compare AI variants
        // against. Mirrors the corpus side-effect of the original
        // BagrutIngestHandler.HandleAsync (lines 167-189). Skipped silently
        // when the curator didn't supply Ministry subject+paper codes —
        // matches the original "missing-metadata-warning" behaviour.
        var corpusWritten = 0;
        if (!string.IsNullOrWhiteSpace(payload.MinistrySubjectCode)
            && !string.IsNullOrWhiteSpace(payload.MinistryQuestionPaperCode)
            && result.Drafts.Count > 0)
        {
            await progress.ReportAsync(90, "Writing Bagrut reference corpus…", ct);
            var corpus = scoped.GetRequiredService<IBagrutCorpusService>();
            var ctx = new BagrutCorpusIngestContext(
                ExamCode: payload.ExamCode,
                MinistrySubjectCode: payload.MinistrySubjectCode!.Trim(),
                MinistryQuestionPaperCode: payload.MinistryQuestionPaperCode!.Trim(),
                Units: payload.Units,
                Year: payload.Year,
                Season: null,
                Moed: null,
                Stream: null,
                DefaultTopicId: payload.TopicId,
                SourceFilename: payload.SourceFilename,
                SourcePdfId: result.PdfId,
                UploadedBy: payload.UploadedBy,
                IngestedAt: DateTimeOffset.UtcNow);
            var corpusItems = BagrutCorpusExtractor.Extract(result.Drafts, ctx);
            if (corpusItems.Count > 0)
            {
                await corpus.UpsertManyAsync(corpusItems, ct);
                corpusWritten = corpusItems.Count;
            }
        }

        await progress.ReportAsync(100,
            $"Completed: {result.Drafts.Count} drafts, {corpusWritten} corpus items", ct);

        return new
        {
            pdfId = result.PdfId,
            examCode = result.ExamCode,
            totalPages = result.TotalPages,
            questionsExtracted = result.QuestionsExtracted,
            figuresExtracted = result.FiguresExtracted,
            persistedDraftIds = ids,
            corpusItemsWritten = corpusWritten,
            warnings = result.Warnings,
        };
    }
}

// Payload for bagrut jobs. Captured by the endpoint and stored as JSON
// inside IngestionJobDocument.PayloadJson; deserialized by the strategy.
// MinistrySubjectCode + MinistryQuestionPaperCode (when both present) trigger
// the BagrutCorpusItemDocument write — feeds the ADR-0043 isomorph rejector.
// IsMinistryReference is independent — a persisted boolean tag on the
// draft document, set by curator at upload time and queryable later.
public sealed record BagrutJobPayload(
    string ExamCode,
    string UploadedBy,
    string? SourceFilename,
    byte[] FileBytes,
    bool IsMinistryReference = false,
    string? MinistrySubjectCode = null,
    string? MinistryQuestionPaperCode = null,
    int? Units = null,
    int? Year = null,
    string? TopicId = null);

// ----- Cloud-dir strategy -----

