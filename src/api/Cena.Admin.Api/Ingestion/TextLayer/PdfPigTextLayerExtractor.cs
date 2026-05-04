// =============================================================================
// Cena Platform — PdfPigTextLayerExtractor (text-layer-first ingestion)
//
// Production implementation of IPdfTextLayerExtractor. Uses UglyToad.PdfPig
// (MIT, .NET-native, zero native deps) to parse the PDF and extract one
// TextLayerPage per PDF page. For the Ministry-of-Education Bagrut PDFs
// (Adobe InDesign-tagged) the embedded text layer is faithful: Hebrew, math
// glyphs, even the bracketed RTL markers (e.g. ‫.1‬ for "question 1").
//
// Reading order:
//   PdfPig's Page.Text reflects the PDF content-stream order, which on
//   InDesign-tagged Bagrut PDFs is the same as the visual reading order
//   (right-to-left lines, top-to-bottom). We use Page.Text directly for
//   RawText; for Blocks we run PdfPig's DefaultWordExtractor and group
//   words into lines by Y-band. We DO NOT reorder anything — Bidi is the
//   renderer's job, and the cover-page / question-marker heuristics
//   operate on Hebrew Unicode characters that survive verbatim.
//
// Encrypted PDFs:
//   PdfPig throws PdfDocumentEncryptedException when no password is
//   supplied. We rethrow as InvalidOperationException with a message that
//   IngestAsync's encrypted-detector matches ("password" / "encrypted") so
//   the caller maps to the structured encrypted_pdf warning shape — same
//   contract as PdfPageRasterizer.
//
// Cancellation:
//   PdfPig is synchronous. We honour ct between page iterations only.
//   A typical Bagrut PDF is 6 pages; latency is dominated by file open
//   (a few ms) so this granularity is sufficient.
//
// Logging:
//   - DEBUG when a PDF opens with HasTextLayer=false (legitimate scan).
//   - WARN when PdfPig throws on a non-encrypted PDF (corrupt/unsupported).
//     The caller falls through to vision-LLM regardless.
//   - ERROR is reserved for the orchestrator (BagrutPdfIngestionService).
// =============================================================================

using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace Cena.Admin.Api.Ingestion.TextLayer;

public sealed class PdfPigTextLayerExtractor : IPdfTextLayerExtractor
{
    private readonly ILogger<PdfPigTextLayerExtractor> _logger;

    public PdfPigTextLayerExtractor(ILogger<PdfPigTextLayerExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<PdfTextLayerExtraction> ExtractAsync(
        byte[] pdfBytes,
        string pdfId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfId);

        if (pdfBytes.Length == 0)
            throw new ArgumentException("PDF bytes are empty", nameof(pdfBytes));

        // PdfPig is synchronous; wrap in Task.Run so a slow open on a
        // large PDF doesn't tie up the request thread. The rest of the
        // pipeline (vision-LLM, cascade) is async, callers expect this to
        // be awaitable.
        return Task.Run(() => ExtractCore(pdfBytes, pdfId, ct), ct);
    }

    private PdfTextLayerExtraction ExtractCore(byte[] pdfBytes, string pdfId, CancellationToken ct)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfBytes);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            // Surface the same keyword the legacy cascade + rasterizer
            // surface so IngestAsync.IsEncryptedRenderError matches.
            throw new InvalidOperationException(
                $"PDF is encrypted (password required): {ex.Message}", ex);
        }

        using (document)
        {
            var pages = new List<TextLayerPage>(document.NumberOfPages);
            long totalChars = 0;

            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                ct.ThrowIfCancellationRequested();

                Page page;
                try
                {
                    page = document.GetPage(pageNumber);
                }
                catch (Exception ex)
                {
                    // Per-page failures are not fatal — emit an empty page
                    // so the page-number line-up stays right and the
                    // downstream caller sees the gap.
                    _logger.LogWarning(ex,
                        "PdfPigTextLayerExtractor: failed to read page {PageNumber} pdf={PdfId} — emitting empty page",
                        pageNumber, pdfId);
                    pages.Add(new TextLayerPage(pageNumber, string.Empty, Array.Empty<TextBlockBbox>()));
                    continue;
                }

                var rawText = page.Text ?? string.Empty;
                var blocks = ExtractBlocks(page);

                pages.Add(new TextLayerPage(
                    PageNumber: pageNumber,
                    RawText: rawText,
                    Blocks: blocks));

                totalChars += CountNonWhitespace(rawText);
            }

            var hasTextLayer = totalChars > PdfTextLayerExtraction.MinTotalTextLength;

            if (!hasTextLayer)
            {
                _logger.LogDebug(
                    "PdfPigTextLayerExtractor: pdf={PdfId} has no usable text layer (total_chars={TotalChars}, threshold={Threshold}) — caller will fall through to vision",
                    pdfId, totalChars, PdfTextLayerExtraction.MinTotalTextLength);
            }
            else
            {
                _logger.LogInformation(
                    "PdfPigTextLayerExtractor: pdf={PdfId} pages={PageCount} total_chars={TotalChars} — text-layer path active",
                    pdfId, pages.Count, totalChars);
            }

            return new PdfTextLayerExtraction(pages, hasTextLayer);
        }
    }

    /// <summary>
    /// Group words into line-blocks via Y-coordinate banding. PdfPig's
    /// default word extractor returns <see cref="Word"/> entries with
    /// (X, Y, W, H) bboxes in PDF user space. We bucket words whose
    /// bbox-Y centres are within 0.5 × line-height of each other into one
    /// line, then emit one TextBlockBbox per line carrying the joined
    /// word text + the line's overall bbox.
    /// </summary>
    private static IReadOnlyList<TextBlockBbox> ExtractBlocks(Page page)
    {
        IEnumerable<Word> words;
        try
        {
            words = page.GetWords();
        }
        catch
        {
            // PdfPig's word extractor is robust but can fail on malformed
            // content streams. Block extraction is best-effort — RawText
            // is what the cover-skip + question-marker heuristics need.
            return Array.Empty<TextBlockBbox>();
        }

        var sorted = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Top)   // top → bottom
            .ThenBy(w => w.BoundingBox.Left)             // tie-break LTR; RTL ordering is irrelevant for Y-banding
            .ToList();

        if (sorted.Count == 0) return Array.Empty<TextBlockBbox>();

        var lines = new List<List<Word>>();
        var current = new List<Word> { sorted[0] };
        var currentYCenter = (sorted[0].BoundingBox.Top + sorted[0].BoundingBox.Bottom) / 2.0;
        var currentHeight = Math.Max(sorted[0].BoundingBox.Height, 1.0);

        for (var i = 1; i < sorted.Count; i++)
        {
            var w = sorted[i];
            var yCenter = (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0;
            var tolerance = Math.Max(currentHeight, Math.Max(w.BoundingBox.Height, 1.0)) * 0.5;
            if (Math.Abs(yCenter - currentYCenter) <= tolerance)
            {
                current.Add(w);
                currentYCenter = (currentYCenter * (current.Count - 1) + yCenter) / current.Count;
                currentHeight = (currentHeight * (current.Count - 1) + w.BoundingBox.Height) / current.Count;
            }
            else
            {
                lines.Add(current);
                current = new List<Word> { w };
                currentYCenter = yCenter;
                currentHeight = Math.Max(w.BoundingBox.Height, 1.0);
            }
        }
        lines.Add(current);

        var blocks = new List<TextBlockBbox>(lines.Count);
        foreach (var line in lines)
        {
            // PDF user space: origin bottom-left. We expose left=min(Left),
            // bottom=min(Bottom), width = max(Right) - min(Left), height = max(Top) - min(Bottom).
            // Concatenation order follows the line's content-stream order
            // (which is what Page.Text relies on too).
            var left = line.Min(w => w.BoundingBox.Left);
            var bottom = line.Min(w => w.BoundingBox.Bottom);
            var right = line.Max(w => w.BoundingBox.Right);
            var top = line.Max(w => w.BoundingBox.Top);

            var text = string.Join(" ", line.Select(w => w.Text));
            blocks.Add(new TextBlockBbox(
                Text: text,
                X: left,
                Y: bottom,
                Width: Math.Max(right - left, 0.0),
                Height: Math.Max(top - bottom, 0.0)));
        }

        return blocks;
    }

    private static int CountNonWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var count = 0;
        foreach (var c in s)
        {
            if (!char.IsWhiteSpace(c)) count++;
        }
        return count;
    }
}
