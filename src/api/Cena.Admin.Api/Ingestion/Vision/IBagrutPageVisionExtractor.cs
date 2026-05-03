// =============================================================================
// Cena Platform — Bagrut page vision extractor contract
//
// Replaces the brittle multi-layer OCR cascade (Surya layout + Surya recognise
// + pix2tex + Gemini vision rescue + Anthropic enhance) with a SINGLE
// vision-LLM tool-use call per rendered PDF page.
//
// Rationale (user report 2026-05-04 ~00:10): the cascade produces chimera
// output on Hebrew + math content — text cuts off mid-sentence at layer
// boundaries, mixed-script tokens are interleaved out of order, and 0
// figures are extracted because Bagrut "diagrams" are inline drawings, not
// separate image objects that Layer2cFigureExtraction can crop. A single
// model call with a closed-set tool schema gets clean Hebrew text, LaTeX
// for the math, and figure bounding boxes the backend then crops.
//
// The contract is page-scoped on purpose — page rasterisation is the
// pipeline boundary (Poppler `pdftoppm` at 200 DPI), and the per-page PNG
// is also the high-quality artifact the SPA renders for source-page
// thumbnails. The cascade integrates at the same place
// (BagrutPdfIngestionService.IngestAsync) so the downstream segmenter,
// draft persistence, and FigureSpecJson serialisation are unchanged.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.Vision;

/// <summary>
/// One figure region detected by the vision-LLM. Bounding box is in IMAGE
/// COORDINATES (pixels, origin top-left) of the rendered page PNG. The
/// backend uses the bbox to crop the figure out of the page PNG and persist
/// it under the same convention as the legacy <c>Layer2cFigureExtraction</c>
/// (atomic write, hash-derived filename, content-addressable).
/// </summary>
/// <param name="X">Top-left X in image coordinates.</param>
/// <param name="Y">Top-left Y in image coordinates.</param>
/// <param name="Width">Bounding-box width in image coordinates.</param>
/// <param name="Height">Bounding-box height in image coordinates.</param>
/// <param name="Kind">"diagram" | "chart" | "table" — caller normalises.</param>
/// <param name="AltText">Short caption-like description from the model. May be null.</param>
public sealed record DetectedFigure(
    double X,
    double Y,
    double Width,
    double Height,
    string Kind,
    string? AltText);

/// <summary>
/// Result of running the vision-LLM on a single rendered PDF page. Mirrors
/// the shape downstream code (BagrutPdfIngestionService.MaterialiseDrafts)
/// already expects from the cascade — text, LaTeX (optional), figures, and
/// a confidence the segmenter and curator UI can sort against.
/// </summary>
/// <param name="PromptText">
/// The question text in source order. Hebrew/Arabic preserves logical
/// Unicode order (the SPA's bidi rendering handles visual reorder); LaTeX
/// math is INLINE inside the text where a transcribed equation appears
/// (delimited by single $...$ for inline, $$...$$ for display).
/// </param>
/// <param name="Latex">
/// Concatenated standalone LaTeX blocks from the page in document order
/// (one block per discrete equation), joined by newline. Null when no
/// math is present. The downstream sanitiser (LaTeXSanitizer) trims and
/// normalises before persistence.
/// </param>
/// <param name="Figures">Zero or more figure regions detected on this page.</param>
/// <param name="Confidence">Self-reported confidence in 0..1.</param>
public sealed record BagrutPageExtraction(
    string PromptText,
    string? Latex,
    IReadOnlyList<DetectedFigure> Figures,
    double Confidence);

/// <summary>
/// Single-call vision-LLM page extractor. Implementations:
/// <list type="bullet">
///   <item><description>Make EXACTLY ONE outbound call per page.</description></item>
///   <item><description>Use a closed-set tool schema (extract_bagrut_page) so a malformed
///   response from the model surfaces as null rather than as text.</description></item>
///   <item><description>NEVER throw on malformed/empty/quota-exhausted output. Return
///   <c>null</c> so the caller can fall back to the legacy cascade.</description></item>
///   <item><description>Fail-open: any catch path returns null (with a WARN log carrying
///   trace_id + pdfId + page).</description></item>
/// </list>
/// </summary>
public interface IBagrutPageVisionExtractor
{
    /// <summary>
    /// Extract one page. <paramref name="pagePngBytes"/> is the rendered PNG
    /// at the configured DPI. <paramref name="pageNumber"/> is 1-based and is
    /// used purely for log correlation (the model does not need to know
    /// which page it is, but the trace does).
    /// </summary>
    /// <returns>
    /// The structured extraction, or <c>null</c> when the call failed for
    /// any reason (no key, breaker open, malformed response, exception,
    /// empty result on a non-empty page). Null means "fall back to legacy
    /// cascade" — never an empty result with an actionable shape.
    /// </returns>
    Task<BagrutPageExtraction?> ExtractAsync(
        ReadOnlyMemory<byte> pagePngBytes,
        int pageNumber,
        string pdfId,
        CancellationToken ct = default);
}
