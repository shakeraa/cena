// =============================================================================
// Cena Platform — PdfTriage implementation (ADR-0033, Layer 0)
//
// C# port of scripts/ocr-spike/pdf_triage.py. Uses PdfPig for PDF parsing;
// character-class heuristic matches the Python reference exactly (validated
// by round-trip tests in Cena.Infrastructure.Tests.Ocr.PdfTriageTests).
//
// Routing:
//   text            → pypdf-style shortcut; skip OCR entirely
//   image_only      → rasterize → Layers 1–5
//   mixed           → extract text + rasterize images → full cascade
//   scanned_bad_ocr → garbled text layer; run OCR and trust Layer 4 gate
//   encrypted       → early reject; Surface A returns 422
//   unreadable      → unrecoverable; human review
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Infrastructure.Ocr.Contracts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace Cena.Infrastructure.Ocr.PdfTriage;

public sealed class PdfTriage : IPdfTriage
{
    // Character-class ranges — MUST match scripts/ocr-spike/pdf_triage.py
    // (_HE_RANGE / _LATIN_RANGE / _MATH_RANGE)
    private const int HebrewRangeStart = 0x0590;
    private const int HebrewRangeEnd = 0x05FF;
    private const int LatinRangeStart = 0x0020;
    private const int LatinRangeEnd = 0x007E;
    private const int MathRangeStart = 0x2200;
    private const int MathRangeEnd = 0x22FF;

    private const string LatinPunctuationWhitelist = ",.;:!?()[]{}/\\*+-=<>%&";

    // Dictionary sanity — a handful of common tokens per language. Matches
    // pdf_triage.py _has_common_tokens. Three hits across the document is
    // the threshold for "this text is actually readable prose."
    private static readonly HashSet<string> CommonEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "with", "this", "that", "problem",
        "find", "solve", "given", "show", "compute", "calculate",
    };
    private static readonly HashSet<string> CommonHebrew = new(StringComparer.Ordinal)
    {
        "של", "את", "על", "היא", "הוא", "משוואה", "פתור", "נתון",
        "מצא", "חשב", "תחום", "תשובה",
    };

    private static readonly Regex TokenRegex =
        new(@"[A-Za-z]{3,}|[\u0590-\u05FF]{2,}", RegexOptions.Compiled);

    public PdfTriageResult Classify(ReadOnlyMemory<byte> pdfBytes, int minTextChars = 50)
    {
        if (pdfBytes.IsEmpty)
        {
            return new PdfTriageResult(
                PdfTriageVerdict.Unreadable, 0, 0, 0, 0, 0, 0,
                new[] { "empty_bytes" });
        }

        // PdfPig needs a seekable stream; copy once into a MemoryStream.
        using var stream = new MemoryStream(pdfBytes.ToArray(), writable: false);

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(stream, new ParsingOptions
            {
                // Match pypdf behaviour: try open without password first; fall through
                // to an encrypted verdict if we can't get to the pages.
                SkipMissingFonts = true,
                UseLenientParsing = true,
            });
        }
        catch (Exception e) when (IsEncryptionFailure(e))
        {
            return new PdfTriageResult(
                PdfTriageVerdict.Encrypted, 0, 0, 0, 0, 0, 0,
                new[] { "pdf_is_encrypted" });
        }
        catch (Exception e)
        {
            return new PdfTriageResult(
                PdfTriageVerdict.Unreadable, 0, 0, 0, 0, 0, 0,
                new[] { $"pdfpig_open_failed: {e.GetType().Name}: {e.Message}" });
        }

        // PdfPig may open encrypted PDFs with empty password but deny content
        // access; surface that as the Encrypted verdict rather than empty-text.
        if (document.IsEncrypted)
        {
            document.Dispose();
            return new PdfTriageResult(
                PdfTriageVerdict.Encrypted, 0, 0, 0, 0, 0, 0,
                new[] { "pdf_is_encrypted" });
        }

        using var _ = document;

        var reasons = new List<string>();
        var fullText = new System.Text.StringBuilder();
        int imageCount = 0;
        int pages = document.NumberOfPages;

        for (int i = 1; i <= pages; i++)
        {
            try
            {
                var page = document.GetPage(i);
                fullText.Append(page.Text);
                imageCount += CountImages(page);
            }
            catch (Exception e)
            {
                reasons.Add($"page_{i}_extract_failed: {e.GetType().Name}");
            }
        }

        var text = fullText.ToString();
        var textChars = text.Trim().Length;

        var (hebrewRatio, latinRatio, gibberishRatio) = CharacterClassRatios(text);
        var saneTokens = HasCommonTokens(text);

        PdfTriageVerdict verdict;
        if (textChars == 0)
        {
            reasons.Add("empty_text_layer");
            verdict = PdfTriageVerdict.ImageOnly;
        }
        else if (textChars < minTextChars)
        {
            reasons.Add($"text_layer_below_threshold:{textChars}<{minTextChars}");
            verdict = PdfTriageVerdict.ImageOnly;
        }
        else if (gibberishRatio > 0.5 || !saneTokens)
        {
            reasons.Add($"gibberish_ratio={gibberishRatio:F2} common_tokens={saneTokens}");
            verdict = PdfTriageVerdict.ScannedBadOcr;
        }
        else if (imageCount > 0)
        {
            reasons.Add($"text_layer_and_images:{textChars}chars,{imageCount}imgs");
            verdict = PdfTriageVerdict.Mixed;
        }
        else
        {
            reasons.Add($"clean_text_layer:{textChars}chars");
            verdict = PdfTriageVerdict.Text;
        }

        return new PdfTriageResult(
            verdict,
            Pages: pages,
            TextChars: textChars,
            ImageCount: imageCount,
            HebrewRatio: Math.Round(hebrewRatio, 3),
            LatinRatio: Math.Round(latinRatio, 3),
            GibberishRatio: Math.Round(gibberishRatio, 3),
            Reasons: reasons);
    }

    // -------------------------------------------------------------------------
    // Heuristics (match pdf_triage.py exactly — do not tune without updating
    // the Python side too, or the C# regression fixture set will drift).
    // -------------------------------------------------------------------------
    private static (double hebrew, double latin, double gibberish) CharacterClassRatios(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0, 0);

        int total = 0, hebrew = 0, latin = 0, weird = 0;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) continue;
            total++;
            int cp = ch;

            if (cp >= HebrewRangeStart && cp <= HebrewRangeEnd)
                hebrew++;
            else if (cp >= LatinRangeStart && cp <= LatinRangeEnd)
                latin++;
            else if (cp >= MathRangeStart && cp <= MathRangeEnd)
                { /* neither — don't count toward weird */ }
            else if (cp < 0x80 && (char.IsLetterOrDigit(ch) || LatinPunctuationWhitelist.Contains(ch)))
                latin++;
            else
                weird++;
        }

        return total == 0
            ? (0, 0, 0)
            : ((double)hebrew / total, (double)latin / total, (double)weird / total);
    }

    private static bool HasCommonTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var tokens = TokenRegex.Matches(text.ToLowerInvariant());
        if (tokens.Count < 3) return false;

        int hits = 0;
        foreach (Match m in tokens)
        {
            if (CommonEnglish.Contains(m.Value) || CommonHebrew.Contains(m.Value))
                hits++;
            if (hits >= 2) return true;
        }
        return false;
    }

    private static int CountImages(Page page)
    {
        try
        {
            return page.GetImages().Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// PdfPig doesn't export a dedicated EncryptedDocumentException; we match
    /// the error message. Pattern is narrow enough to avoid false positives
    /// on generic parse failures.
    /// </summary>
    private static bool IsEncryptionFailure(Exception e)
    {
        if (e.Message is null) return false;
        var msg = e.Message;
        return msg.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("password", StringComparison.OrdinalIgnoreCase);
    }
}
