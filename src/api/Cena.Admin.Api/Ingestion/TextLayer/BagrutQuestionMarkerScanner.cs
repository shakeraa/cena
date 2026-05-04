// =============================================================================
// Cena Platform — BagrutQuestionMarkerScanner (text-layer-first ingestion)
//
// Detects `שאלה N` (or visually-equivalent) markers in a Bagrut PDF page's
// raw text. The Ministry-of-Education Bagrut format opens each question with
// `שאלה N` (Hebrew "Question N", e.g. שאלה 1, שאלה 2). After PdfPig text-
// layer extraction the marker often surfaces in one of three shapes:
//
//   Shape A (logical Hebrew, common in pdftotext + some text layers):
//     "שאלה 1" — Hebrew word "question" + digit, separated by whitespace.
//
//   Shape B (visual-reversed Hebrew, common in PdfPig on InDesign-tagged
//   exam PDFs where the content stream is laid out in visual RTL order):
//     "הלאש 1" — same word, character-reversed.
//
//   Shape C (numeric-only labels, the most reliable signal in this corpus):
//     " .N " or " .N." — a period followed by a single digit 1-9, preceded
//     by whitespace and followed by whitespace OR another period.
//     This is what InDesign actually emits in the content stream for the
//     marker glyph block; PdfPig surfaces it as a 2-char run.
//     We require a NON-digit char before the period so we don't false-
//     positive a decimal literal like "0.45" or "1.6".
//
// All three shapes are checked. The scanner is robust to short-circuit
// detection (HasMarker) and ordered enumeration (FindMarkers), used by:
//   - BagrutCoverPageHeuristic (HasMarker → "this page is NOT cover")
//   - the multi-question splitter (FindMarkers → split offsets within page)
//
// Why digits 1-9 only:
//   Bagrut math exams cap at 8 questions per booklet. Allowing two-digit
//   markers (10+) creates more false positives than it catches; the closed
//   range matches the corpus.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.Ingestion.TextLayer;

internal static partial class BagrutQuestionMarkerScanner
{
    private const string HebrewQuestionLogical = "שאלה";   // logical order
    private const string HebrewQuestionVisual  = "הלאש";   // visual-reversed (PdfPig output)

    // Numeric-only marker: NOT preceded by digit/period/letter (rejects
    // decimal continuations like "0.45" / "1.6"), then a literal period,
    // a single digit 1-9, and NOT followed by another digit (rejects
    // ".123"). Captures the digit in group 1.
    //
    // Negative lookahead `(?!\d)` (vs the earlier `(?=[\s.]|$)`) tolerates
    // Unicode bidi-formatting chars (U+202A LRE, U+202C POP, U+200E LRM,
    // …) immediately after the digit. Verified empirically against
    // corpus/tests/35581-q.pdf page 2 where Poppler `pdftotext -layout`
    // wraps each marker in LRE…POP pairs and the previous lookahead
    // refused to match because POP is neither `\s`, `.`, nor `$`.
    [GeneratedRegex(@"(?<![0-9.\p{L}])\.([1-9])(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDotMarkerRegex();

    // Hebrew word + space + digit (1-9). Both logical and visual-reversed.
    [GeneratedRegex(@"(שאלה|הלאש)\s*([1-9])(?:\b|$)", RegexOptions.CultureInvariant)]
    private static partial Regex HebrewWordMarkerRegex();

    /// <summary>
    /// True iff the page's raw text contains at least one question marker.
    /// </summary>
    public static bool HasMarker(string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return false;
        if (rawText.Contains(HebrewQuestionLogical, StringComparison.Ordinal)
            && HebrewWordMarkerRegex().IsMatch(rawText))
        {
            return true;
        }
        if (rawText.Contains(HebrewQuestionVisual, StringComparison.Ordinal)
            && HebrewWordMarkerRegex().IsMatch(rawText))
        {
            return true;
        }
        return NumericDotMarkerRegex().IsMatch(rawText);
    }

    /// <summary>
    /// One marker hit on a page.
    /// </summary>
    /// <param name="Index">0-based offset into the page raw text where the marker starts.</param>
    /// <param name="QuestionNumber">The detected question number (1-9).</param>
    /// <param name="MatchLength">Length of the matched marker substring.</param>
    public sealed record MarkerHit(int Index, int QuestionNumber, int MatchLength);

    /// <summary>
    /// Enumerate all question markers in the page's raw text, in text order.
    /// Returns an ordered list with no duplicates at the same Index.
    /// </summary>
    public static IReadOnlyList<MarkerHit> FindMarkers(string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return Array.Empty<MarkerHit>();

        var hits = new List<MarkerHit>();
        var seenIndexes = new HashSet<int>();

        // Hebrew word forms — preferred when present (carry the explicit
        // word + digit; less ambiguous than numeric-only).
        foreach (Match m in HebrewWordMarkerRegex().Matches(rawText))
        {
            if (!int.TryParse(m.Groups[2].Value, out var n)) continue;
            if (n < 1 || n > 9) continue;
            if (seenIndexes.Add(m.Index))
                hits.Add(new MarkerHit(m.Index, n, m.Length));
        }

        // Numeric-only fallback. Skip a numeric marker that lands within
        // 8 chars of a Hebrew-word marker for the same N (avoids double-
        // counting "שאלה 1 ... .1" near each other; the corpus does not
        // produce this overlap, but we belt-and-brace).
        foreach (Match m in NumericDotMarkerRegex().Matches(rawText))
        {
            if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
            if (n < 1 || n > 9) continue;

            var dup = false;
            foreach (var h in hits)
            {
                if (h.QuestionNumber == n && Math.Abs(h.Index - m.Index) < 8)
                {
                    dup = true;
                    break;
                }
            }
            if (dup) continue;

            if (seenIndexes.Add(m.Index))
                hits.Add(new MarkerHit(m.Index, n, m.Length));
        }

        hits.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        return hits;
    }
}
