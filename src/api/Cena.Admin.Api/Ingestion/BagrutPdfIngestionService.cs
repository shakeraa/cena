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

using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.PdfTriage;
using Cena.Infrastructure.Security;
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
    private readonly IOcrCascadeService _cascade;
    private readonly ILogger<BagrutPdfIngestionService> _logger;

    public BagrutPdfIngestionService(
        IOcrCascadeService cascade,
        ILogger<BagrutPdfIngestionService> logger)
    {
        _cascade = cascade;
        _logger  = logger;
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
        var drafts = ExtractQuestions(pages, examCode, out var figureCount);

        var warnings = BuildWarnings(result, drafts);

        _logger.LogInformation(
            "Bagrut ingestion complete: pdf={PdfId} pages={Pages} drafts={Drafts} figures={Figures} review={Review} fallbacks=[{Fallbacks}]",
            pdfId, pages.Count, drafts.Count, figureCount,
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
    // helpers
    // --------------------------------------------------------------------
    private static IReadOnlyList<ExtractedPage> BuildExtractedPages(OcrCascadeResult result)
    {
        // Group blocks by bbox.Page (1-indexed). Blocks without a bbox are
        // treated as page 1 (common for scanned single-page inputs).
        var textByPage = result.TextBlocks
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .GroupBy(t => t.Bbox?.Page ?? 1)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Text!).ToList());

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
                ? string.Join("\n", tList)
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

    private static List<IngestionDraftQuestion> ExtractQuestions(
        IReadOnlyList<ExtractedPage> pages,
        string examCode,
        out int figureCount)
    {
        figureCount = pages.Sum(p => p.Figures.Count);
        // Heuristics-based question boundary detection:
        //   - "שאלה X" (Hebrew: "Question X")
        //   - "Problem X" / "Question X" / numbered "1." | "1)" at line start
        // For the first pass we keep it simple: one draft per page. The
        // curator trims/splits during the review handshake (RDY-019e) once
        // CuratorMetadata lands. A smarter LLM-backed segmenter is a
        // Phase-3 enhancement (RDY-019c coverage report feeds back).
        var drafts = new List<IngestionDraftQuestion>();

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.RawText) && string.IsNullOrWhiteSpace(page.ExtractedLatex))
                continue;

            var sanitizedLatex = string.IsNullOrWhiteSpace(page.ExtractedLatex)
                ? null
                : LaTeXSanitizer.Sanitize(page.ExtractedLatex);

            var promptPreview = string.IsNullOrWhiteSpace(page.RawText)
                ? (sanitizedLatex ?? string.Empty)
                : (page.RawText.Length > 400 ? page.RawText[..400] + "…" : page.RawText);

            var reviewNotes = new List<string> { "bagrut-reference:auto-extracted;requires-curator-recreation" };
            if (page.OcrConfidence < 0.70) reviewNotes.Add("low-ocr-confidence");
            if (page.Figures.Count > 0) reviewNotes.Add($"figures:{page.Figures.Count}");

            var figureSpec = page.Figures.Count == 0
                ? null
                : System.Text.Json.JsonSerializer.Serialize(new
                {
                    figures = page.Figures.Select(f => new
                    {
                        page = f.PageNumber,
                        kind = f.Kind,
                        croppedPath = f.CroppedPath,
                        bbox = new { x = f.X, y = f.Y, w = f.Width, h = f.Height },
                        altText = f.AltText
                    }).ToArray()
                });

            drafts.Add(new IngestionDraftQuestion(
                DraftId: $"draft-{examCode}-p{page.PageNumber}-{Guid.NewGuid().ToString("N")[..6]}",
                SourcePage: page.PageNumber,
                Prompt: promptPreview,
                LatexContent: sanitizedLatex,
                AnswerChoices: Array.Empty<string>(),
                CorrectAnswer: null,
                ExamCode: examCode,
                FigureSpecJson: figureSpec,
                ExtractionConfidence: page.OcrConfidence,
                ReviewNotes: reviewNotes.ToArray()));
        }

        return drafts;
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
}
