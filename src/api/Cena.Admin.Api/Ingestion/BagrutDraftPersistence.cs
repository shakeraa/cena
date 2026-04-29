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
//
// Curator metadata auto-fill (2026-04-29):
//   The Bagrut PDF flow knows the Subject/Language/SourceType/Track
//   deterministically — math is the only Bagrut subject we accept today,
//   the corpus is Hebrew, the source is by definition a Bagrut reference
//   PDF, and the track is encoded in the examCode prefix
//   ("math-5u-2026-35581" → 5u). We populate AutoExtractedMetadata at
//   persist time so the curator UI opens with the five required fields
//   pre-filled instead of forcing manual data-entry on every upload.
//   Strategy = "bagrut_path_inference_v1". TaxonomyNode is set to the
//   track-level placeholder (e.g. "math_5u" matching scripts/
//   bagrut-taxonomy.json) — the curator MUST drill down to a subtopic
//   before confirming, so its confidence is intentionally low (0.40).
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Ingest;
using Cena.Api.Contracts.Admin.Ingestion;
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
        bool isMinistryReference = false,
        CancellationToken ct = default);
}

public sealed class BagrutDraftPersistence : IBagrutDraftPersistence
{
    private readonly IDocumentStore _store;
    private readonly ILogger<BagrutDraftPersistence> _logger;

    // examCode shape we recognise: "math-5u-..." / "math-4u-..." / "math-3u-..."
    // Anchored at start, case-insensitive. The "5u" fragment is what
    // CuratorMetadata.Track expects (matches the dropdown wire values
    // in CuratorMetadataPanel.vue: 3u/4u/5u). We don't use \b after the
    // track group because in .NET regex \b doesn't fire between word chars,
    // so "math_5u_2026" would fail (the underscore after "5u" is a word
    // char). Instead we require a separator (-/_/.) or end-of-string.
    private static readonly Regex BagrutExamCodeRx = new(
        @"^math[-_](?<track>3u|4u|5u)(?:[-_.]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Strategy identifier persisted onto the document so the UI / future
    // analytics can see how a metadata set was inferred. Bumped when the
    // inference rules change (e.g. moving from path-based to OCR-driven).
    public const string ExtractionStrategy = "bagrut_path_inference_v1";

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
        bool isMinistryReference = false,
        CancellationToken ct = default)
    {
        if (drafts.Count == 0) return Array.Empty<string>();

        var now = DateTimeOffset.UtcNow;
        var ids = new List<string>(drafts.Count);

        // Pre-compute curator metadata from the examCode — same inference
        // for every draft in this upload, so we only do the parse once.
        var (track, trackConfidence) = ParseTrack(examCode);
        var (taxonomyNode, taxonomyConfidence) = ParseTaxonomyRoot(examCode);

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
                AutoExtractedMetadata = new PipelineCuratorMetadata
                {
                    Subject         = "math",                // Bagrut PDF flow is math-only today
                    Language        = "he",                  // Bagrut papers are Hebrew (primary)
                    Track           = track,                 // parsed from examCode prefix
                    SourceType      = "bagrut_reference",    // by definition for this code path
                    TaxonomyNode    = taxonomyNode,          // track-level placeholder (e.g. "math_5u")
                    ExpectedFigures = true,                  // Bagrut math nearly always carries figures
                },
                MetadataFieldConfidences = new Dictionary<string, double>
                {
                    [nameof(CuratorMetadata.Subject)]         = 0.95,
                    [nameof(CuratorMetadata.Language)]        = 0.95,
                    [nameof(CuratorMetadata.Track)]           = trackConfidence,
                    [nameof(CuratorMetadata.SourceType)]      = 0.99,
                    [nameof(CuratorMetadata.TaxonomyNode)]    = taxonomyConfidence,
                    [nameof(CuratorMetadata.ExpectedFigures)] = 0.70,
                },
                MetadataExtractionStrategy = ExtractionStrategy,
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
                IsMinistryReference = isMinistryReference,
                ReviewNotes = d.ReviewNotes.ToList(),
                CreatedAt = now,
            });
        }

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Persisted {Count} Bagrut drafts to PipelineItemDocument + BagrutDraftPayloadDocument (examCode={ExamCode}, pdfId={PdfId}, track={Track}, taxonomy={Taxonomy})",
            drafts.Count, examCode, sourcePdfId, track ?? "(unknown)", taxonomyNode ?? "(unknown)");

        return ids;
    }

    // --------------------------------------------------------------------
    // Curator metadata inference
    // --------------------------------------------------------------------

    /// <summary>
    /// Parses the track ("3u" | "4u" | "5u") out of an examCode of the form
    /// "math-{track}-..." (e.g. "math-5u-2026-35581" → "5u"). Returns a
    /// (null, 0.5) fallback when the prefix doesn't match — the curator
    /// must then pick the track manually.
    /// </summary>
    internal static (string? Track, double Confidence) ParseTrack(string examCode)
    {
        if (string.IsNullOrWhiteSpace(examCode)) return (null, 0.5);
        var m = BagrutExamCodeRx.Match(examCode);
        if (!m.Success) return (null, 0.5);
        return (m.Groups["track"].Value.ToLowerInvariant(), 0.95);
    }

    /// <summary>
    /// Track-level taxonomy root key matching scripts/bagrut-taxonomy.json
    /// ("math_3u" | "math_4u" | "math_5u"). Curator MUST drill down to a
    /// subtopic (e.g. "math_5u.calculus.derivative_rules") before
    /// confirming, so confidence stays low (0.40).
    /// </summary>
    internal static (string? TaxonomyNode, double Confidence) ParseTaxonomyRoot(string examCode)
    {
        var (track, _) = ParseTrack(examCode);
        if (track is null) return (null, 0.2);
        return ($"math_{track}", 0.40);
    }
}
