// =============================================================================
// Cena Platform — IPdfTriage (ADR-0033, Layer 0)
//
// Classifies a PDF into one of five categories so Layer 0 can route away
// from OCR when a clean text layer is already present. Corresponds to
// scripts/ocr-spike/pdf_triage.py. Pure C# — no native deps.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.PdfTriage;

public interface IPdfTriage
{
    /// <summary>
    /// Inspect the PDF bytes and return a triage verdict. Never throws
    /// on malformed input — unreadable files return <see cref="PdfTriageVerdict.Unreadable"/>.
    /// </summary>
    PdfTriageResult Classify(ReadOnlyMemory<byte> pdfBytes, int minTextChars = 50);
}

public sealed record PdfTriageResult(
    PdfTriageVerdict Verdict,
    int Pages,
    int TextChars,
    int ImageCount,
    double HebrewRatio,
    double LatinRatio,
    double GibberishRatio,
    IReadOnlyList<string> Reasons);
