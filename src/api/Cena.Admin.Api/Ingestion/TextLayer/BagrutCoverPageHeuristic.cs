// =============================================================================
// Cena Platform — BagrutCoverPageHeuristic (text-layer-first ingestion)
//
// Decides whether a Bagrut PDF page is the exam cover / instructions /
// formula-sheet preamble (and therefore should be SKIPPED from drafts).
// User-reported defect on 35581-q.pdf: the cover page was being surfaced as
// "Question 1" because the legacy one-draft-per-populated-page segmenter had
// no signal for cover content.
//
// Rules (ALL must hold for a page to be flagged as cover):
//   1. Page index ≤ 2.
//   2. No `שאלה N` marker present (so we don't false-positive a real
//      question that happens to mention "הוראות"/"דפי נוסחאות" in its body).
//   3. Either:
//        a. text contains BOTH `הוראות` (instructions) AND `משך הבחינה`
//           (exam duration), OR
//        b. text contains `דפי נוסחאות` (formula sheet).
//
// Rule 1 is intentional — we never skip page 3+ even if the text matches the
// patterns. Bagrut exams ALWAYS have the cover material on pages 1-2, never
// later. A late-page false positive would be a worse outcome than a missed
// cover skip.
//
// Hebrew encoding caveat (PdfPig vs pdftotext):
//   PdfPig surfaces Hebrew character runs in VISUAL (right-to-left) order
//   on InDesign-tagged content streams — i.e. each word's characters are
//   reversed compared to the logical Unicode order. The cover keywords are
//   matched in BOTH forms so the heuristic works regardless of which
//   extraction path produced the text.
//
//     logical form (pdftotext, some PdfPig outputs)
//          הוראות     →   visual-reversed (PdfPig on InDesign tagged):  תוארוה
//          משך הבחינה  →   הניחבה ךשמ
//          דפי נוסחאות →   תואחסונ יפד
//
// Detection runs on the page's RawText only — bboxes are not relevant to the
// cover/question distinction.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.TextLayer;

internal static class BagrutCoverPageHeuristic
{
    // Hebrew literals — matched as substrings against page raw text. The
    // Bagrut PDF Unicode is NFC so plain Contains() with Ordinal comparison
    // is correct (and faster than a regex with culture-sensitive matching).
    // Both logical (left-to-right characters in memory order) and visual-
    // reversed (right-to-left, what PdfPig produces on InDesign-tagged
    // content) forms are accepted.
    //
    // Multi-word phrases are decomposed into AND-ed individual words so
    // tab/whitespace differences across PdfPig outputs don't break the
    // match (35581-q.pdf separates with a single space; 35582-q.pdf
    // separates with TABs in the cover line).
    private static readonly string[] InstructionsForms = { "הוראות", "תוארוה" };
    private static readonly string[] ExamDurationFirstWord  = { "משך",  "ךשמ" };  // logical | reversed
    private static readonly string[] ExamDurationSecondWord = { "הבחינה", "הניחבה" };
    private static readonly string[] FormulaSheetFirstWord  = { "דפי",   "יפד" };
    private static readonly string[] FormulaSheetSecondWord = { "נוסחאות", "תואחסונ" };

    /// <summary>
    /// True iff the page should be skipped from question drafts.
    /// </summary>
    /// <param name="pageNumber">1-indexed PDF page number.</param>
    /// <param name="rawText">The page's full raw text from the text layer.</param>
    /// <param name="reason">
    /// On true: a short machine-readable reason ("instructions+duration",
    /// "formula-sheet"). On false: an empty string.
    /// </param>
    public static bool IsCoverPage(int pageNumber, string rawText, out string reason)
    {
        reason = string.Empty;

        // Rule 1: only the first two pages are eligible.
        if (pageNumber > 2) return false;

        if (string.IsNullOrEmpty(rawText)) return false;

        // Rule 2: never skip a page that has a question marker — even if
        // the text mentions instructions/formula keywords in the body
        // (e.g. "השתמשו בנוסחה...").
        if (BagrutQuestionMarkerScanner.HasMarker(rawText)) return false;

        // Rule 3a: instructions + exam duration together = exam cover.
        var hasInstructions = ContainsAny(rawText, InstructionsForms);
        var hasExamDuration =
            ContainsAny(rawText, ExamDurationFirstWord) &&
            ContainsAny(rawText, ExamDurationSecondWord);
        if (hasInstructions && hasExamDuration)
        {
            reason = "instructions+duration";
            return true;
        }

        // Rule 3b: explicit formula-sheet preamble. Two-word match — same
        // tab-tolerance reason as ExamDuration above.
        if (ContainsAny(rawText, FormulaSheetFirstWord) &&
            ContainsAny(rawText, FormulaSheetSecondWord))
        {
            reason = "formula-sheet";
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
