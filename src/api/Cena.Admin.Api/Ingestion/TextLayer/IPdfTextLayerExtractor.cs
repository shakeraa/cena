// =============================================================================
// Cena Platform — IPdfTextLayerExtractor (text-layer-first ingestion)
//
// Cheapest path to "what's in this Bagrut PDF": parse the PDF's embedded text
// layer directly. Adobe InDesign-tagged Ministry-of-Education Bagrut exam
// PDFs (the 100% case for our reference corpus) have a faithful text layer
// — Hebrew, Arabic, math, the lot — and PdfPig pulls it out deterministically
// at zero LLM cost. This interface is consumed by BagrutPdfIngestionService
// BEFORE the vision-LLM extractor and BEFORE the multi-layer cascade.
//
// When a PDF has no usable text layer (rasterised scans, image-only inputs)
// HasTextLayer returns false and the caller falls through to the vision
// extractor.
//
// Contract:
//   - One TextLayerPage per PDF page, in page order, 1-indexed.
//   - RawText preserves Hebrew/Arabic Unicode verbatim — no normalisation,
//     no transliteration. Reading order is the PDF's own content-stream
//     order (which on InDesign-tagged Bagrut PDFs is logical order).
//   - Blocks expose per-line bboxes in PDF user space (origin bottom-left,
//     points). The cover-page heuristic + multi-question splitter operate
//     on RawText only; bboxes are stored for downstream consumers (visual
//     review, future structural analysis).
//   - Encrypted PDFs throw (matches existing IngestAsync behaviour) — the
//     caller maps to the encrypted_pdf warning.
//   - Implementations MUST NOT throw on a missing-text-layer scan PDF;
//     instead set HasTextLayer=false and return whatever pages PdfPig
//     could open with empty RawText.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.TextLayer;

/// <summary>
/// One page of text-layer extraction output.
/// </summary>
/// <param name="PageNumber">1-indexed page number (matches PDF page numbering).</param>
/// <param name="RawText">
/// Full extracted text for the page, in the PDF's own content order.
/// Empty (not null) when the page has no text layer.
/// </param>
/// <param name="Blocks">
/// Per-line bboxes captured during extraction. Each block carries the
/// substring of <see cref="RawText"/> it represents and the bbox in PDF
/// user space (points, origin bottom-left). Non-null but possibly empty.
/// </param>
public sealed record TextLayerPage(
    int PageNumber,
    string RawText,
    IReadOnlyList<TextBlockBbox> Blocks);

/// <summary>
/// One block (typically one line) of extracted text with its bbox.
/// </summary>
/// <param name="Text">The text content of the block.</param>
/// <param name="X">Left-most X in PDF user space (points, origin bottom-left).</param>
/// <param name="Y">Bottom-most Y in PDF user space (points, origin bottom-left).</param>
/// <param name="Width">Width in points.</param>
/// <param name="Height">Height in points.</param>
public sealed record TextBlockBbox(
    string Text,
    double X,
    double Y,
    double Width,
    double Height);

/// <summary>
/// Aggregate result of text-layer extraction for one PDF.
/// </summary>
/// <param name="Pages">One entry per PDF page.</param>
/// <param name="HasTextLayer">
/// True iff the total non-whitespace character count across all pages
/// exceeds <see cref="MinTotalTextLength"/>. The caller treats false as
/// "this PDF has no usable text layer" and falls through to the vision
/// extractor.
/// </param>
public sealed record PdfTextLayerExtraction(
    IReadOnlyList<TextLayerPage> Pages,
    bool HasTextLayer)
{
    /// <summary>
    /// Threshold (non-whitespace characters across all pages combined)
    /// below which a PDF is treated as having no text layer. 200 is the
    /// briefed value — empirically below 200 chars we are looking at a
    /// scan PDF that PdfPig opened but extracted only artifact text.
    /// </summary>
    public const int MinTotalTextLength = 200;
}

/// <summary>
/// Extract the embedded text layer of a PDF.
/// </summary>
public interface IPdfTextLayerExtractor
{
    /// <summary>
    /// Parse <paramref name="pdfBytes"/> with PdfPig and return per-page
    /// text + per-line bboxes. Throws when the PDF is encrypted (matches
    /// the existing IngestAsync behaviour). Never throws on a scan-only
    /// PDF — returns HasTextLayer=false instead.
    /// </summary>
    Task<PdfTextLayerExtraction> ExtractAsync(
        byte[] pdfBytes,
        string pdfId,
        CancellationToken ct = default);
}
