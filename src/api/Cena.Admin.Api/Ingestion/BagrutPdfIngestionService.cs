// =============================================================================
// Cena Platform — Bagrut PDF Ingestion Pipeline (PHOTO-002 / Phase 2.3)
//
// Admin tool: upload a Bagrut exam PDF → run the real OCR cascade
// (IOcrCascadeService, ADR-0033) → create QuestionDocument draft records
// with figure specs + LaTeX content.
//
// Pre-Phase-2.3 this file had a placeholder `OcrPageAsync` that returned
// empty output and a comment "// Production: call Mathpix API or Gemini
// Vision for math-aware OCR". That placeholder is gone — the cascade now
// runs for real, routing through Layers 0–5 with CAS validation on the
// math blocks.
//
// Surface B (admin batch) rules (ADR-0033):
//   surface        = CascadeSurface.AdminBatch
//   source_type    = bagrut_reference (per memory: bagrut-reference-only)
//   catastrophic τ = 0.40  (looser than Surface A because admin curators review)
//   encrypted PDF  → cascade returns Triage=Encrypted, HumanReviewRequired=true
//                    → we surface an empty draft list + warning
//   circuit open   → OcrCircuitOpenException propagates to caller
//   input error    → OcrInputException propagates to caller
//
// The ingested PDF is NEVER shipped to students as-is. Bagrut content is
// reference material only; student-facing items are AI-authored CAS-gated
// recreations (see ADR and memory pointer project_bagrut_reference_only).
// The draft records this service produces feed the admin review queue,
// where a curator authors the final question.
//
// NO STUBS, NO MOCKS. Real cascade, real figure storage, real sanitizer.
// =============================================================================

using Cena.Admin.Api.Ingestion.Segmenter;
using Cena.Admin.Api.Ingestion.TextLayer;
using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.PdfTriage;
using Cena.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion;

/// <summary>
/// A page extracted from a Bagrut exam PDF. Figures land in storage (per
/// FigureStorageOptions); here we carry the reference, not the bytes.
/// </summary>
public record ExtractedPage(
    int PageNumber,
    string RawText,
    string? ExtractedLatex,
    IReadOnlyList<ExtractedFigure> Figures,
    double OcrConfidence
);

/// <summary>
/// A figure extracted from a PDF page. CroppedPath points at a bbox crop
/// written by Layer 2c. Bytes are NOT inlined — the review UI pulls them.
/// </summary>
public record ExtractedFigure(
    int PageNumber,
    double X, double Y, double Width, double Height,
    string? CroppedPath,
    string Kind,
    string? AltText
);

/// <summary>
/// A draft question extracted from a Bagrut PDF. CuratorMetadata from the
/// review handshake (RDY-019e) is attached later — this record is the raw
/// output immediately after OCR + heuristic question boundary detection.
/// </summary>
public record IngestionDraftQuestion(
    string DraftId,
    int SourcePage,
    string Prompt,
    string? LatexContent,
    string[] AnswerChoices,
    string? CorrectAnswer,
    string ExamCode,
    string? FigureSpecJson,
    double ExtractionConfidence,
    string[] ReviewNotes
);

/// <summary>
/// Result of PDF ingestion. Warnings include low-confidence hints, CAS
/// failures, and encrypted-PDF markers — the curator reads them before
/// approving drafts.
/// </summary>
public record PdfIngestionResult(
    string PdfId,
    string ExamCode,
    int TotalPages,
    int QuestionsExtracted,
    int FiguresExtracted,
    IReadOnlyList<IngestionDraftQuestion> Drafts,
    string[] Warnings
);

public interface IBagrutPdfIngestionService
{
    /// <summary>
    /// Process a Bagrut exam PDF and extract draft questions.
    /// </summary>
    Task<PdfIngestionResult> IngestAsync(
        byte[] pdfBytes,
        string examCode,
        string uploadedBy,
        CancellationToken ct = default);
}

/// <summary>
/// Bagrut PDF ingestion pipeline: PDF → real OCR cascade → draft questions.
/// </summary>
public sealed class BagrutPdfIngestionService : IBagrutPdfIngestionService
{
    /// <summary>
    /// Feature flag: when true, the per-page vision-LLM extractor replaces
    /// the multi-layer cascade for non-encrypted PDFs. Default OFF in prod;
    /// hot-reload overlay defaults ON so curators can validate output.
    /// Mirrors <see cref="GeminiVisionPageExtractor.EnabledFlagKey"/>.
    /// </summary>
    public const string VisionExtractorFlagKey = "Cena:Ingestion:BagrutVisionExtractorEnabled";

    /// <summary>
    /// Feature flag for the PdfPig-based text-layer-first extractor
    /// (claude-subagent-text-layer / pdfpig-first). Default ON in prod and
    /// hot-reload — text-layer is the cheap, deterministic, perfect path
    /// for the 100%-of-corpus InDesign-tagged Ministry-of-Education Bagrut
    /// PDFs. The path is fail-open: when the extractor reports no usable
    /// text layer (genuine scan PDFs) the orchestrator falls through to
    /// the vision-LLM extractor.
    /// </summary>
    public const string TextLayerExtractorFlagKey = "Cena:Ingestion:BagrutTextLayerExtractorEnabled";

    /// <summary>
    /// Sanity ceiling on the per-segment prompt text. Curators trim at
    /// review, so we DON'T truncate at the legacy 401-char preview point
    /// (user-reported defect — vision-good output was being chopped to a
    /// short preview before drafts persisted). 16,000 covers a multi-part
    /// Bagrut question + sub-questions; longer than that signals a bug
    /// upstream and we hard-cap to keep the BagrutDraftPayloadDocument
    /// row size bounded.
    /// </summary>
    private const int PromptSanityCeiling = 16_000;

    private readonly IOcrCascadeService _cascade;
    private readonly IBagrutPdfStore _pdfStore;
    private readonly IBagrutQuestionSegmenter _segmenter;
    private readonly ILogger<BagrutPdfIngestionService> _logger;

    // Vision-extractor seam (optional — when any of these are missing or
    // the flag is off, the legacy cascade runs unchanged).
    private readonly IConfiguration? _configuration;
    private readonly IPdfPageRasterizer? _rasterizer;
    private readonly IBagrutPageVisionExtractor? _visionExtractor;
    private readonly IFigureCropper? _figureCropper;

    // Text-layer-first seam (optional — when missing or the flag is off,
    // the orchestrator runs the vision-extractor → cascade chain unchanged).
    private readonly IPdfTextLayerExtractor? _textLayerExtractor;

    /// <summary>
    /// Production ctor. <paramref name="segmenter"/> is REQUIRED and is the
    /// single point of policy for question-boundary detection (replaced the
    /// inline one-draft-per-page heuristic 2026-05-03 — user-reported defect
    /// 35581-q.pdf produced 6 drafts where pages 1-2 were exam cover and
    /// "answer 5 of 8" preamble).
    /// <para>
    /// Vision-extractor parameters (rasterizer, visionExtractor, figureCropper,
    /// configuration) are optional. When all four are wired AND the
    /// <see cref="VisionExtractorFlagKey"/> flag is true, IngestAsync runs
    /// rasterize → vision → crop instead of the multi-layer cascade. When any
    /// one is missing OR the flag is false OR any vision step fails, the
    /// legacy cascade runs unchanged (fail-open by construction).
    /// </para>
    /// </summary>
    public BagrutPdfIngestionService(
        IOcrCascadeService cascade,
        IBagrutPdfStore pdfStore,
        IBagrutQuestionSegmenter segmenter,
        ILogger<BagrutPdfIngestionService> logger,
        IConfiguration? configuration = null,
        IPdfPageRasterizer? rasterizer = null,
        IBagrutPageVisionExtractor? visionExtractor = null,
        IFigureCropper? figureCropper = null,
        IPdfTextLayerExtractor? textLayerExtractor = null)
    {
        ArgumentNullException.ThrowIfNull(cascade);
        ArgumentNullException.ThrowIfNull(pdfStore);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(logger);

        _cascade   = cascade;
        _pdfStore  = pdfStore;
        _segmenter = segmenter;
        _logger    = logger;
        _configuration = configuration;
        _rasterizer = rasterizer;
        _visionExtractor = visionExtractor;
        _figureCropper = figureCropper;
        _textLayerExtractor = textLayerExtractor;
    }

    public async Task<PdfIngestionResult> IngestAsync(
        byte[] pdfBytes,
        string examCode,
        string uploadedBy,
        CancellationToken ct = default)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
            throw new ArgumentException("PDF bytes are empty", nameof(pdfBytes));
        if (string.IsNullOrWhiteSpace(examCode))
            throw new ArgumentException("Exam code is required", nameof(examCode));

        var pdfId = GeneratePdfId(pdfBytes);
        _logger.LogInformation(
            "Bagrut ingestion start: pdf={PdfId} exam={ExamCode} size_kb={SizeKb} uploader={Uploader}",
            pdfId, examCode, pdfBytes.Length / 1024, uploadedBy);

        // Persist the source PDF before OCR runs. Curators need the original
        // bytes to do side-by-side visual review against the extracted output.
        // Content-addressable: same id = same bytes, so re-ingesting an
        // identical PDF is a no-op write. Failure here is logged but does
        // not abort ingestion — the OCR result is still useful, the visual
        // review surface degrades to "PDF not retained — re-upload".
        try
        {
            await _pdfStore.PersistAsync(pdfId, pdfBytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Bagrut PDF persistence failed (continuing ingestion): pdf={PdfId}", pdfId);
        }

        // Text-layer-first branch (claude-subagent-text-layer / pdfpig-first,
        // 2026-05-04). When the PdfPig extractor is wired AND the flag is on,
        // try the embedded text layer FIRST. For Adobe InDesign-tagged
        // Ministry-of-Education Bagrut PDFs (100% of the reference corpus)
        // the text layer returns Hebrew + math content perfectly; we don't
        // need an LLM round-trip or OCR. When the PDF has no usable text
        // layer (real scans), the path returns null and we fall through to
        // the vision extractor unchanged.
        if (IsTextLayerExtractorEnabled())
        {
            var textLayerResult = await TryRunTextLayerExtractorAsync(
                pdfBytes, pdfId, examCode, ct).ConfigureAwait(false);
            if (textLayerResult is not null)
            {
                return textLayerResult;
            }
            // textLayerResult==null is the fail-open path: extractor
            // reported no usable text layer (likely a scan PDF) OR threw.
            // The vision-extractor branch picks up below.
        }

        // Vision-extractor branch (vision-extractor task, 2026-05-04). When
        // all seams are wired AND the flag is on, replace the multi-layer
        // cascade with rasterize → vision-LLM → crop. Output shape into the
        // segmenter is byte-identical so the downstream code path is
        // unchanged.
        if (IsVisionExtractorEnabled())
        {
            var visionResult = await TryRunVisionExtractorAsync(
                pdfBytes, pdfId, examCode, ct).ConfigureAwait(false);
            if (visionResult is not null)
            {
                return visionResult;
            }
            // visionResult==null is the fail-open path — log already
            // emitted by TryRunVisionExtractorAsync; legacy cascade picks up
            // below.
        }

        var hints = new OcrContextHints(
            Subject: "math",
            Language: null,                       // cascade auto-detects
            Track: null,                          // set by curator metadata later
            SourceType: SourceType.BagrutReference,
            TaxonomyNode: null,
            ExpectedFigures: true);               // Bagrut exams regularly contain figures

        OcrCascadeResult result;
        try
        {
            result = await _cascade.RecognizeAsync(
                bytes: pdfBytes,
                contentType: "application/pdf",
                hints: hints,
                surface: CascadeSurface.AdminBatch,
                ct: ct);
        }
        catch (OcrInputException ex)
        {
            _logger.LogWarning(ex, "Bagrut ingestion input error: pdf={PdfId}", pdfId);
            throw;
        }
        catch (OcrCircuitOpenException ex)
        {
            _logger.LogError(ex,
                "Bagrut ingestion aborted — OCR cloud fallbacks unavailable (circuit open). pdf={PdfId}",
                pdfId);
            throw;
        }

        // Encrypted PDF: cascade returns a result with Triage=Encrypted and
        // HumanReviewRequired=true. We do NOT throw — we surface an empty
        // draft list + structured warning so the admin UI can tell the
        // curator the file needs decryption.
        if (result.PdfTriage == PdfTriageVerdict.Encrypted)
        {
            _logger.LogWarning("Bagrut PDF encrypted, no drafts extracted: pdf={PdfId}", pdfId);
            return new PdfIngestionResult(
                PdfId: pdfId,
                ExamCode: examCode,
                TotalPages: 0,
                QuestionsExtracted: 0,
                FiguresExtracted: 0,
                Drafts: Array.Empty<IngestionDraftQuestion>(),
                Warnings: new[] { "encrypted_pdf:cannot_read_without_password" });
        }

        var pages = BuildExtractedPages(result);

        // Segmenter owns question-boundary detection. The default impl in
        // production (LlmBagrutQuestionSegmenter when the flag is on) calls
        // Haiku once per PDF and falls back to OneDraftPerPageSegmenter on
        // every failure path — segmentation NEVER blocks ingestion.
        var segments = await _segmenter.SegmentAsync(pages, examCode, pdfId, ct).ConfigureAwait(false);
        var drafts = MaterialiseDrafts(pages, segments, examCode, out var figureCount);

        var warnings = BuildWarnings(result, drafts);

        _logger.LogInformation(
            "Bagrut ingestion complete: pdf={PdfId} pages={Pages} segments={Segments} drafts={Drafts} figures={Figures} review={Review} fallbacks=[{Fallbacks}]",
            pdfId, pages.Count, segments.Count, drafts.Count, figureCount,
            result.HumanReviewRequired, string.Join(",", result.FallbacksFired));

        return new PdfIngestionResult(
            PdfId: pdfId,
            ExamCode: examCode,
            TotalPages: pages.Count,
            QuestionsExtracted: drafts.Count,
            FiguresExtracted: figureCount,
            Drafts: drafts,
            Warnings: warnings);
    }

    // --------------------------------------------------------------------
    // Text-layer-first path (claude-subagent-text-layer / pdfpig-first,
    // 2026-05-04)
    //
    // Returns null when:
    //   - the extractor seam is missing (test scaffolding)
    //   - the flag is off
    //   - the extractor threw
    //   - the PDF has no usable text layer (HasTextLayer=false)
    //   - the text layer ran but produced 0 questions across all pages
    //     after cover-skipping (fail-loud — don't silently produce empty
    //     drafts when the extraction itself succeeded)
    //
    // Encrypted PDFs surface the same encrypted_pdf warning as the vision
    // and cascade branches.
    // --------------------------------------------------------------------
    private bool IsTextLayerExtractorEnabled()
    {
        if (_textLayerExtractor is null) return false;
        if (_configuration is null) return true; // default ON when no config wired (test convenience)
        var raw = _configuration[TextLayerExtractorFlagKey];
        if (string.IsNullOrEmpty(raw)) return true; // default ON when key absent
        return !bool.TryParse(raw, out var enabled) || enabled;
    }

    private async Task<PdfIngestionResult?> TryRunTextLayerExtractorAsync(
        byte[] pdfBytes,
        string pdfId,
        string examCode,
        CancellationToken ct)
    {
        // 1. Run PdfPig. Encrypted PDF surfaces as InvalidOperationException
        //    with a message containing "encrypted"/"password" — same shape
        //    the rasterizer raises so the encrypted-detector matches.
        PdfTextLayerExtraction extraction;
        try
        {
            extraction = await _textLayerExtractor!.ExtractAsync(pdfBytes, pdfId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsEncryptedRenderError(ex.Message))
        {
            _logger.LogWarning(
                "Bagrut text-layer-path: PDF encrypted, no drafts extracted: pdf={PdfId} error={Err}",
                pdfId, ex.Message);
            return new PdfIngestionResult(
                PdfId: pdfId,
                ExamCode: examCode,
                TotalPages: 0,
                QuestionsExtracted: 0,
                FiguresExtracted: 0,
                Drafts: Array.Empty<IngestionDraftQuestion>(),
                Warnings: new[] { "encrypted_pdf:cannot_read_without_password" });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Bagrut text-layer-path: extractor threw pdf={PdfId} — falling through to vision/cascade",
                pdfId);
            return null;
        }

        // 2. Reject scan PDFs early (HasTextLayer=false) so the vision
        //    path is given a fair chance.
        if (!extraction.HasTextLayer)
        {
            _logger.LogInformation(
                "Bagrut text-layer-path: pdf={PdfId} has no usable text layer (pages={PageCount}) — falling through to vision/cascade",
                pdfId, extraction.Pages.Count);
            return null;
        }

        // 3. Build ExtractedPages — but BEFORE materialising drafts apply
        //    the cover-page heuristic and the multi-question splitter.
        //    Pages classified as cover are kept in the page list (so the
        //    OCR confidence figure / PdfId page numbering stays right) but
        //    excluded from the segment list.
        var pages = new List<ExtractedPage>(extraction.Pages.Count);
        var segments = new List<Segmenter.BagrutQuestionSegment>(extraction.Pages.Count);
        var skippedCovers = 0;

        foreach (var p in extraction.Pages)
        {
            ct.ThrowIfCancellationRequested();

            pages.Add(new ExtractedPage(
                PageNumber: p.PageNumber,
                RawText: p.RawText,
                ExtractedLatex: null, // text-layer doesn't separate math; the curator extracts on review
                Figures: Array.Empty<ExtractedFigure>(),
                OcrConfidence: 0.99)); // text-layer is deterministic + faithful

            // Skip blank pages (empty text-layer page — happens when a PDF
            // has a back cover or a "page intentionally left blank").
            if (string.IsNullOrWhiteSpace(p.RawText)) continue;

            // Cover-page heuristic — exam preamble / instructions /
            // formula sheet. Logged INFO so curators can audit.
            if (BagrutCoverPageHeuristic.IsCoverPage(p.PageNumber, p.RawText, out var coverReason))
            {
                _logger.LogInformation(
                    "Bagrut text-layer-path: skipping cover page pdf={PdfId} page={Page} reason=cover-heuristic:{Reason}",
                    pdfId, p.PageNumber, coverReason);
                skippedCovers++;
                continue;
            }

            // Multi-question per page — split by `שאלה N` markers.
            var slices = BagrutPageQuestionSplitter.Split(p.RawText);
            if (slices.Count == 0)
            {
                continue; // empty after split — extremely rare; skip.
            }

            for (var sliceIdx = 0; sliceIdx < slices.Count; sliceIdx++)
            {
                var s = slices[sliceIdx];
                if (string.IsNullOrWhiteSpace(s.Text)) continue;

                segments.Add(new Segmenter.BagrutQuestionSegment(
                    StartPage: p.PageNumber,
                    EndPage: p.PageNumber,
                    QuestionLabel: s.QuestionNumber.HasValue
                        ? $"שאלה {s.QuestionNumber.Value}"
                        : null,
                    Confidence: 0.99)
                {
                    // Only stamp IntraPageIndex when the page truly has 2+
                    // segments; preserves the legacy draft-id scheme for
                    // the common 1-segment-per-page case.
                    IntraPageIndex = slices.Count > 1 ? sliceIdx : null,
                });
            }
        }

        // 4. Fail-loud-on-extraction-success: text-layer ran, has content,
        //    but produced 0 segments after cover-skip + split. That's a
        //    contract violation — at least one question is expected.
        //    Falling through to vision is the right move; logging ERROR
        //    makes the regression visible to the curator UI ribbon.
        if (segments.Count == 0)
        {
            _logger.LogError(
                "Bagrut text-layer-path: extracted text but produced 0 questions pdf={PdfId} pages={PageCount} skipped_covers={SkippedCovers} — falling through to vision/cascade",
                pdfId, extraction.Pages.Count, skippedCovers);
            return null;
        }

        // 5. Materialise drafts using the text-layer-aware path. Slice
        //    text per question goes into the prompt directly (no truncation).
        var sliceLookup = BuildTextLayerSliceLookup(extraction.Pages);
        var drafts = MaterialiseTextLayerDrafts(segments, sliceLookup, examCode);

        var warnings = new List<string>();
        if (skippedCovers > 0) warnings.Add($"text_layer_cover_skipped:{skippedCovers}");
        if (drafts.Count != segments.Count) warnings.Add("some_segments_dropped");

        _logger.LogInformation(
            "Bagrut ingestion complete (text-layer-path): pdf={PdfId} pages={Pages} segments={Segments} drafts={Drafts} skipped_covers={SkippedCovers}",
            pdfId, pages.Count, segments.Count, drafts.Count, skippedCovers);

        return new PdfIngestionResult(
            PdfId: pdfId,
            ExamCode: examCode,
            TotalPages: pages.Count,
            QuestionsExtracted: drafts.Count,
            FiguresExtracted: 0, // text-layer path does not detect figures (vision-LLM does)
            Drafts: drafts,
            Warnings: warnings.ToArray());
    }

    private static Dictionary<(int Page, int? Slot), string> BuildTextLayerSliceLookup(
        IReadOnlyList<TextLayerPage> pages)
    {
        var dict = new Dictionary<(int, int?), string>();
        foreach (var p in pages)
        {
            var slices = BagrutPageQuestionSplitter.Split(p.RawText);
            if (slices.Count == 0) continue;
            if (slices.Count == 1)
            {
                dict[(p.PageNumber, null)] = slices[0].Text;
            }
            else
            {
                for (var i = 0; i < slices.Count; i++)
                    dict[(p.PageNumber, i)] = slices[i].Text;
            }
        }
        return dict;
    }

    private static List<IngestionDraftQuestion> MaterialiseTextLayerDrafts(
        IReadOnlyList<Segmenter.BagrutQuestionSegment> segments,
        IReadOnlyDictionary<(int Page, int? Slot), string> sliceLookup,
        string examCode)
    {
        var drafts = new List<IngestionDraftQuestion>(segments.Count);

        foreach (var segment in segments)
        {
            segment.Validate();

            var key = (segment.StartPage, segment.IntraPageIndex);
            if (!sliceLookup.TryGetValue(key, out var sliceText))
            {
                // Defensive — the segment cites a page+slot that the
                // splitter didn't surface. Skip rather than emit a
                // phantom draft; logging at a higher level so the curator
                // notices.
                continue;
            }

            if (string.IsNullOrWhiteSpace(sliceText)) continue;

            // No truncation at ingestion (user-reported defect — 401-char
            // cap removed). Hard sanity ceiling defends the read-model row.
            var prompt = sliceText.Length > PromptSanityCeiling
                ? sliceText[..PromptSanityCeiling] + "…"
                : sliceText;

            var reviewNotes = new List<string>
            {
                "bagrut-reference:auto-extracted;requires-curator-recreation",
                "extractor:text-layer",
            };
            if (segment.IntraPageIndex.HasValue)
                reviewNotes.Add($"intra-page-index:{segment.IntraPageIndex.Value}");
            if (!string.IsNullOrWhiteSpace(segment.QuestionLabel))
                reviewNotes.Add($"segmenter-label:{segment.QuestionLabel}");

            // Draft id is stable across re-uploads of the same exam +
            // start-page (+ intra-page slot when set). Two-question pages
            // produce -p2-q0 / -p2-q1 instead of colliding on -p2.
            var idSuffix = segment.IntraPageIndex.HasValue
                ? $"p{segment.StartPage}-q{segment.IntraPageIndex.Value}"
                : $"p{segment.StartPage}";

            drafts.Add(new IngestionDraftQuestion(
                DraftId: $"draft-{examCode}-{idSuffix}",
                SourcePage: segment.StartPage,
                Prompt: prompt,
                LatexContent: null,
                AnswerChoices: Array.Empty<string>(),
                CorrectAnswer: null,
                ExamCode: examCode,
                FigureSpecJson: null,
                ExtractionConfidence: segment.Confidence,
                ReviewNotes: reviewNotes.ToArray()));
        }

        return drafts;
    }

    // --------------------------------------------------------------------
    // Vision-extractor path (vision-extractor branch, 2026-05-04)
    //
    // Returns null when the vision path was not viable (any seam missing,
    // any per-page extraction failed, encrypted PDF). Caller falls back to
    // the legacy cascade.
    // --------------------------------------------------------------------
    private bool IsVisionExtractorEnabled()
    {
        if (_configuration is null) return false;
        if (_rasterizer is null) return false;
        if (_visionExtractor is null) return false;
        if (_figureCropper is null) return false;
        var raw = _configuration[VisionExtractorFlagKey];
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private async Task<PdfIngestionResult?> TryRunVisionExtractorAsync(
        byte[] pdfBytes,
        string pdfId,
        string examCode,
        CancellationToken ct)
    {
        // 1. Rasterize. Throws InvalidOperationException for encrypted /
        //    missing-binary; we map encrypted to the structured warning
        //    and any other failure to fall-back-to-cascade.
        IReadOnlyList<string> pagePaths;
        try
        {
            pagePaths = await _rasterizer!.RasterizeAsync(pdfBytes, pdfId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsEncryptedRenderError(ex.Message))
        {
            _logger.LogWarning(
                "Bagrut vision-path: PDF encrypted, no drafts extracted: pdf={PdfId} error={Err}",
                pdfId, ex.Message);
            return new PdfIngestionResult(
                PdfId: pdfId,
                ExamCode: examCode,
                TotalPages: 0,
                QuestionsExtracted: 0,
                FiguresExtracted: 0,
                Drafts: Array.Empty<IngestionDraftQuestion>(),
                Warnings: new[] { "encrypted_pdf:cannot_read_without_password" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Bagrut vision-path: rasterizer failed pdf={PdfId} — falling back to legacy cascade",
                pdfId);
            return null;
        }

        if (pagePaths.Count == 0)
        {
            _logger.LogWarning(
                "Bagrut vision-path: rasterizer produced 0 pages pdf={PdfId} — falling back to legacy cascade",
                pdfId);
            return null;
        }

        // 2. Per-page vision extract. Any null result → fall back. Empty
        //    extraction (whitespace promptText with 0 figures and no
        //    LaTeX) on a page fails the same way — we trust the cascade
        //    over an empty vision result.
        var pages = new List<ExtractedPage>(pagePaths.Count);
        for (var i = 0; i < pagePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pageNumber = i + 1;
            byte[] pageBytes;
            try
            {
                pageBytes = await File.ReadAllBytesAsync(pagePaths[i], ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bagrut vision-path: page PNG read failed pdf={PdfId} page={Page} — falling back to legacy cascade",
                    pdfId, pageNumber);
                return null;
            }

            BagrutPageExtraction? extraction;
            try
            {
                extraction = await _visionExtractor!.ExtractAsync(
                    pageBytes, pageNumber, pdfId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bagrut vision-path: vision extractor threw pdf={PdfId} page={Page} — falling back to legacy cascade",
                    pdfId, pageNumber);
                return null;
            }

            if (extraction is null)
            {
                _logger.LogWarning(
                    "Bagrut vision-path: vision extractor returned null pdf={PdfId} page={Page} — falling back to legacy cascade",
                    pdfId, pageNumber);
                return null;
            }

            // 3. Crop figures. Persists under FigureStorageOptions.OutputDirectory
            //    so the existing VisualReviewEndpoints figure-stream resolves
            //    without changes.
            IReadOnlyList<CroppedFigureRecord> cropped;
            try
            {
                cropped = await _figureCropper!.CropAsync(
                    pagePaths[i], pageNumber, pdfId, extraction.Figures, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bagrut vision-path: figure cropper threw pdf={PdfId} page={Page} — keeping page text but dropping figures",
                    pdfId, pageNumber);
                cropped = Array.Empty<CroppedFigureRecord>();
            }

            var figures = cropped
                .Select(c => new ExtractedFigure(
                    PageNumber: c.PageNumber,
                    X: c.X, Y: c.Y, Width: c.Width, Height: c.Height,
                    CroppedPath: c.CroppedPath,
                    Kind: c.Kind,
                    AltText: c.AltText))
                .ToList();

            pages.Add(new ExtractedPage(
                PageNumber: pageNumber,
                RawText: extraction.PromptText,
                ExtractedLatex: extraction.Latex,
                Figures: figures,
                OcrConfidence: extraction.Confidence));
        }

        // 4. Run segmenter on the vision-built pages — same code path as
        //    cascade output. The segmenter doesn't care which extractor
        //    produced the ExtractedPage records.
        var segments = await _segmenter.SegmentAsync(pages, examCode, pdfId, ct).ConfigureAwait(false);
        var drafts = MaterialiseDrafts(pages, segments, examCode, out var figureCount);

        var warnings = new List<string>();
        if (drafts.Any(d => d.ExtractionConfidence < 0.70))
            warnings.Add("some_drafts_low_confidence");

        _logger.LogInformation(
            "Bagrut ingestion complete (vision-path): pdf={PdfId} pages={Pages} segments={Segments} drafts={Drafts} figures={Figures}",
            pdfId, pages.Count, segments.Count, drafts.Count, figureCount);

        return new PdfIngestionResult(
            PdfId: pdfId,
            ExamCode: examCode,
            TotalPages: pages.Count,
            QuestionsExtracted: drafts.Count,
            FiguresExtracted: figureCount,
            Drafts: drafts,
            Warnings: warnings.ToArray());
    }

    private static bool IsEncryptedRenderError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        // pdftoppm surfaces encrypted/locked PDFs with messages like
        // "Command Line Error: Incorrect password" / "Document not unlocked".
        // We match the literal cues; anything else is treated as a
        // non-encryption render failure.
        return message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unlocked", StringComparison.OrdinalIgnoreCase)
            || message.Contains("encrypted", StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------
    private static IReadOnlyList<ExtractedPage> BuildExtractedPages(OcrCascadeResult result)
    {
        // Group blocks by bbox.Page (1-indexed). Blocks without a bbox are
        // treated as page 1 (common for scanned single-page inputs).
        // Keep the full OcrTextBlock here (not just text) so JoinTextBlocks
        // can use bbox.Y/X for line + reading-order reconstruction.
        var textByPage = result.TextBlocks
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .GroupBy(t => t.Bbox?.Page ?? 1)
            .ToDictionary(g => g.Key, g => g.ToList());

        var mathByPage = result.MathBlocks
            .Where(m => !string.IsNullOrWhiteSpace(m.Latex))
            .GroupBy(m => m.Bbox?.Page ?? 1)
            .ToDictionary(g => g.Key, g => g.ToList());

        var figuresByPage = result.Figures
            .GroupBy(f => f.Bbox.Page)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allPages = textByPage.Keys
            .Union(mathByPage.Keys)
            .Union(figuresByPage.Keys)
            .OrderBy(p => p)
            .ToList();

        if (allPages.Count == 0) allPages.Add(1);

        var pages = new List<ExtractedPage>(allPages.Count);
        foreach (var pageNumber in allPages)
        {
            var text = textByPage.TryGetValue(pageNumber, out var tList)
                ? JoinTextBlocks(tList)
                : string.Empty;

            // Concatenate validated LaTeX for a quick preview; fall back
            // to all math on the page if none validated.
            string? latex = null;
            if (mathByPage.TryGetValue(pageNumber, out var mList))
            {
                var validated = mList.Where(m => m.SympyParsed).ToList();
                var chosen = validated.Count > 0 ? validated : mList;
                latex = string.Join("\n", chosen.Select(m => m.Latex));
            }

            var figs = figuresByPage.TryGetValue(pageNumber, out var fList)
                ? fList.Select(f => new ExtractedFigure(
                    PageNumber: pageNumber,
                    X: f.Bbox.X,
                    Y: f.Bbox.Y,
                    Width: f.Bbox.W,
                    Height: f.Bbox.H,
                    CroppedPath: f.CroppedPath,
                    Kind: f.Kind,
                    AltText: f.Caption)).ToList()
                : new List<ExtractedFigure>();

            // Page confidence: if we have math, use average math conf
            // (which reflects SymPy validation); else use text conf avg.
            double confidence;
            if (mathByPage.TryGetValue(pageNumber, out var mAll))
            {
                confidence = mAll.Average(m => m.Confidence);
            }
            else
            {
                var blocks = result.TextBlocks
                    .Where(t => (t.Bbox?.Page ?? 1) == pageNumber)
                    .ToList();
                confidence = blocks.Count > 0 ? blocks.Average(b => b.Confidence) : result.OverallConfidence;
            }

            pages.Add(new ExtractedPage(
                PageNumber: pageNumber,
                RawText: text,
                ExtractedLatex: string.IsNullOrWhiteSpace(latex) ? null : latex,
                Figures: figs,
                OcrConfidence: confidence));
        }

        return pages;
    }

    /// <summary>
    /// Project segmenter output into IngestionDraftQuestion records. The
    /// segmenter owns boundary detection ONLY; this method owns:
    ///   - LaTeX sanitisation per page,
    ///   - per-segment figureSpec JSON aggregation,
    ///   - draft-id idempotency (stable across re-uploads),
    ///   - review-note tagging (low confidence, figures, multi-page span,
    ///     LLM-supplied label),
    ///   - confidence reduction across constituent pages (max OCR confidence
    ///     wins — a multi-page question's signal-strength is its strongest
    ///     page, not its weakest).
    ///
    /// Pages referenced by a segment that are not populated (no text + no
    /// LaTeX) are silently included in the multi-page concatenation — the
    /// segmenter is the authority on which pages belong together; if it
    /// thinks a blank page is part of the question, it stays.
    /// </summary>
    private static List<IngestionDraftQuestion> MaterialiseDrafts(
        IReadOnlyList<ExtractedPage> pages,
        IReadOnlyList<Segmenter.BagrutQuestionSegment> segments,
        string examCode,
        out int figureCount)
    {
        // Figures are counted across the WHOLE document (matches the prior
        // contract — figureCount on PdfIngestionResult is "figures extracted
        // from the PDF", not "figures attached to drafts"). Pages skipped by
        // the segmenter (cover, instructions) still contributed figures to
        // the OCR layer; counting them keeps the warning surface honest.
        figureCount = pages.Sum(p => p.Figures.Count);

        var drafts = new List<IngestionDraftQuestion>(segments.Count);
        var pageByNumber = pages.ToDictionary(p => p.PageNumber);

        foreach (var segment in segments)
        {
            // Defense in depth: a segmenter that violates its own contract
            // would otherwise silently emit a draft for a non-existent page.
            // Validate throws ArgumentOutOfRangeException; let it bubble so
            // the bug is visible in test output rather than producing a
            // mis-shaped draft.
            segment.Validate();

            // Collect the constituent pages that the segmenter bundled.
            var constituent = new List<ExtractedPage>(segment.EndPage - segment.StartPage + 1);
            for (var p = segment.StartPage; p <= segment.EndPage; p++)
            {
                if (pageByNumber.TryGetValue(p, out var page))
                    constituent.Add(page);
            }

            if (constituent.Count == 0)
            {
                // Segmenter pointed at pages that are not in the OCR result.
                // The LLM segmenter validates this and falls back; the
                // OneDraftPerPage segmenter cannot produce this case.
                // Skip rather than emit a phantom draft.
                continue;
            }

            // Skip a fully-empty segment (every constituent page has no
            // text and no LaTeX) — matches the prior heuristic's behavior.
            var anyPopulated = constituent.Any(p =>
                !string.IsNullOrWhiteSpace(p.RawText) ||
                !string.IsNullOrWhiteSpace(p.ExtractedLatex));
            if (!anyPopulated) continue;

            // Concatenate raw text across constituent pages (page break is
            // a single newline). NO 401-char truncation here — the user
            // reported this as a defect (clean vision-LLM output was being
            // chopped at the ingestion boundary). Curators trim at review.
            // Hard sanity ceiling at PromptSanityCeiling defends the read
            // model row; longer than that signals a bug upstream.
            var rawTextJoined = string.Join(
                "\n",
                constituent
                    .Select(p => p.RawText ?? string.Empty)
                    .Where(t => !string.IsNullOrWhiteSpace(t)));

            // Concatenate LaTeX too — multi-page derivations have their
            // equations split across pages; sanitising each page's LaTeX
            // independently then joining preserves layer-2c canonical form.
            var sanitizedLatex = SanitizeAndJoinLatex(constituent);

            var promptText = string.IsNullOrWhiteSpace(rawTextJoined)
                ? (sanitizedLatex ?? string.Empty)
                : rawTextJoined;
            if (promptText.Length > PromptSanityCeiling)
            {
                promptText = promptText[..PromptSanityCeiling] + "…";
            }

            var reviewNotes = new List<string> { "bagrut-reference:auto-extracted;requires-curator-recreation" };

            // Confidence is the strongest signal across the segment's
            // pages. Multi-page questions where one page is high-quality
            // and another is messy should not be flagged low-confidence
            // because of the messy page alone; the curator trusts the
            // strong page and uses the messy one as auxiliary context.
            var maxOcrConfidence = constituent.Max(p => p.OcrConfidence);
            if (maxOcrConfidence < 0.70) reviewNotes.Add("low-ocr-confidence");

            var totalFigures = constituent.Sum(p => p.Figures.Count);
            if (totalFigures > 0) reviewNotes.Add($"figures:{totalFigures}");

            if (constituent.Count > 1)
            {
                reviewNotes.Add(
                    $"multi-page-segment:{segment.StartPage}-{segment.EndPage}");
            }

            if (!string.IsNullOrWhiteSpace(segment.QuestionLabel))
            {
                reviewNotes.Add($"segmenter-label:{segment.QuestionLabel}");
            }

            // Aggregate figureSpec across constituent pages so the curator's
            // visual review surface attaches every figure that belongs to
            // the question, not just the first page's figures.
            var allFigures = constituent.SelectMany(p => p.Figures).ToList();
            var figureSpec = allFigures.Count == 0
                ? null
                : System.Text.Json.JsonSerializer.Serialize(new
                {
                    figures = allFigures.Select(f => new
                    {
                        page = f.PageNumber,
                        kind = f.Kind,
                        croppedPath = f.CroppedPath,
                        bbox = new { x = f.X, y = f.Y, w = f.Width, h = f.Height },
                        altText = f.AltText
                    }).ToArray()
                });

            drafts.Add(new IngestionDraftQuestion(
                // Idempotent draft id: stable across re-uploads of the
                // same (examCode, startPage). Marten upserts on Id
                // collision, so re-running the same Bagrut PDF refreshes
                // the draft in place. Keyed on startPage (not the segment
                // span) so a segmenter that later refines a multi-page
                // boundary still hits the same draft id — keeps curator
                // metadata attached across re-extraction.
                DraftId: $"draft-{examCode}-p{segment.StartPage}",
                SourcePage: segment.StartPage,
                Prompt: promptText,
                LatexContent: sanitizedLatex,
                AnswerChoices: Array.Empty<string>(),
                CorrectAnswer: null,
                ExamCode: examCode,
                FigureSpecJson: figureSpec,
                ExtractionConfidence: maxOcrConfidence,
                ReviewNotes: reviewNotes.ToArray()));
        }

        return drafts;
    }

    private static string? SanitizeAndJoinLatex(IReadOnlyList<ExtractedPage> constituent)
    {
        var pieces = new List<string>(constituent.Count);
        foreach (var p in constituent)
        {
            if (string.IsNullOrWhiteSpace(p.ExtractedLatex)) continue;
            var sanitized = LaTeXSanitizer.Sanitize(p.ExtractedLatex);
            if (!string.IsNullOrWhiteSpace(sanitized)) pieces.Add(sanitized);
        }
        return pieces.Count == 0 ? null : string.Join("\n", pieces);
    }

    private static string[] BuildWarnings(
        OcrCascadeResult result,
        IReadOnlyList<IngestionDraftQuestion> drafts)
    {
        var warnings = new List<string>();

        foreach (var reason in result.ReasonsForReview)
            warnings.Add($"review:{reason}");

        foreach (var fb in result.FallbacksFired)
            warnings.Add($"fallback_used:{fb}");

        if (result.CasFailedMath > 0)
            warnings.Add($"cas_failed:{result.CasFailedMath}");

        if (drafts.Any(d => d.ExtractionConfidence < 0.70))
            warnings.Add("some_drafts_low_confidence");

        if (result.HumanReviewRequired)
            warnings.Add("human_review_required");

        return warnings.ToArray();
    }

    private static string GeneratePdfId(byte[] pdfBytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(pdfBytes);
        return $"pdf-{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    /// <summary>
    /// Reconstruct readable prose from OCR text blocks. The cascade emits
    /// one block per token (Mathpix/Gemini behaviour), so the previous
    /// `string.Join("\n", ...)` rendered every word on its own line in
    /// the curator panel (2026-05-02 user report).
    ///
    /// Strategy:
    ///   1. Group blocks into lines using bbox.Y proximity. A block joins
    ///      the running line when its Y center lies within a tolerance
    ///      band proportional to the line's average glyph height. This is
    ///      cheap and robust against small baseline jitter without merging
    ///      paragraphs.
    ///   2. Within each line, sort by X. RTL-dominant lines (Hebrew /
    ///      Arabic) sort X descending so that logical Unicode order
    ///      matches reading order; LTR lines sort ascending. Mixed lines
    ///      go LTR — the BiDi algorithm handles visual reorder at render
    ///      time.
    ///   3. Lines join with `\n`, blocks within a line join with a single
    ///      space. Tokens are trimmed so we don't get "word  word".
    ///
    /// Blocks without a bbox (rare — typically scanned single-page
    /// inputs) appear at the end joined with spaces.
    /// </summary>
    internal static string JoinTextBlocks(IReadOnlyList<OcrTextBlock> blocks)
    {
        if (blocks.Count == 0) return string.Empty;

        var withBox = blocks
            .Where(b => b.Bbox is not null && !string.IsNullOrWhiteSpace(b.Text))
            .ToList();
        var withoutBox = blocks
            .Where(b => b.Bbox is null && !string.IsNullOrWhiteSpace(b.Text))
            .Select(b => b.Text!.Trim())
            .ToList();

        if (withBox.Count == 0)
            return string.Join(" ", withoutBox);

        // Sort by Y first (top→bottom), then X (left→right) as a stable
        // baseline. Line-grouping below picks up runs that share Y.
        var sorted = withBox
            .OrderBy(b => b.Bbox!.Y)
            .ThenBy(b => b.Bbox!.X)
            .ToList();

        var lines = new List<List<OcrTextBlock>>();
        var currentLine = new List<OcrTextBlock> { sorted[0] };
        double lineYCenter = sorted[0].Bbox!.Y + sorted[0].Bbox!.H / 2.0;
        double lineHeight  = Math.Max(sorted[0].Bbox!.H, 1.0);

        for (int i = 1; i < sorted.Count; i++)
        {
            var bb = sorted[i].Bbox!;
            var yCenter = bb.Y + bb.H / 2.0;
            // Tolerance = half the larger of (line's running height, this
            // block's height). Lets a 12pt main-line absorb a 10pt
            // adjacent token; rejects a 12pt token sitting one line down.
            var tolerance = Math.Max(lineHeight, Math.Max(bb.H, 1.0)) * 0.5;
            if (Math.Abs(yCenter - lineYCenter) <= tolerance)
            {
                currentLine.Add(sorted[i]);
                // Re-average so the running center tracks the line's
                // actual centroid as more tokens join.
                lineYCenter = (lineYCenter * (currentLine.Count - 1) + yCenter) / currentLine.Count;
                lineHeight  = (lineHeight  * (currentLine.Count - 1) + bb.H) / currentLine.Count;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = new List<OcrTextBlock> { sorted[i] };
                lineYCenter = yCenter;
                lineHeight  = Math.Max(bb.H, 1.0);
            }
        }
        lines.Add(currentLine);

        var lineStrings = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            // RTL-dominant when ≥50% of blocks on this line are RTL.
            // Mixed-script falls back to LTR sort + BiDi at render time.
            var rtlShare = line.Count(b => b.IsRtl) / (double)line.Count;
            var ordered = rtlShare >= 0.5
                ? line.OrderByDescending(b => b.Bbox!.X)
                : line.OrderBy(b => b.Bbox!.X);
            var joined = string.Join(" ", ordered.Select(b => b.Text!.Trim()));
            if (!string.IsNullOrWhiteSpace(joined))
                lineStrings.Add(joined);
        }

        var result = string.Join("\n", lineStrings);
        if (withoutBox.Count > 0)
            result += (result.Length > 0 ? "\n" : "") + string.Join(" ", withoutBox);
        return result;
    }
}
