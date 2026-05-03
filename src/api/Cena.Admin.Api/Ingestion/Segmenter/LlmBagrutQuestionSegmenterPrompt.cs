// =============================================================================
// Cena Platform — LlmBagrutQuestionSegmenterPrompt (system + user prompts)
//
// Split out so the prompt strings are unit-testable in isolation (the
// LlmBagrutQuestionSegmenter integration test pins that the rubric mentions
// every Bagrut-specific cue we expect Haiku to recognise).
//
// Rubric design notes:
//   - Hebrew-first (Bagrut is a Hebrew exam by default), with English fallbacks
//     because the OCR cascade sometimes detects Latin transliterations on
//     mixed-script pages.
//   - Closed-set output (start_page, end_page, label) — boundaries only; the
//     downstream pipeline owns LaTeX sanitisation, figure spec JSON, and
//     review notes.
//   - Explicit list of pages to SKIP (cover, instructions, "answer N of M",
//     answer-key) because the user-reported defect (35581-q.pdf, 2026-05-03)
//     was that those exact page types produced phantom drafts under the
//     prior one-draft-per-page heuristic.
// =============================================================================

using System.Globalization;
using System.Text;

namespace Cena.Admin.Api.Ingestion.Segmenter;

/// <summary>
/// Builds the static system prompt and the per-PDF user prompt sent to the
/// LLM-tier segmenter.
/// </summary>
internal static class LlmBagrutQuestionSegmenterPrompt
{
    /// <summary>
    /// Static system prompt — cached aggressively (cache_control: ephemeral
    /// on the system block). Changing this string invalidates the prompt
    /// cache; keep edits minimal.
    /// </summary>
    public const string SystemPrompt = """
        You are segmenting a Hebrew Bagrut mathematics exam PDF that has been
        OCR'd page-by-page.

        Your single job: identify which OCR pages host QUESTIONS and how the
        pages group into questions, then emit a JSON list of segments via the
        `segment_bagrut_questions` tool. Do not narrate, do not paraphrase
        the questions, do not solve anything.

        ## Rules

        1. SKIP these page types entirely (do NOT emit a segment for them):
           - exam cover / front matter (title, exam code, date, level),
           - "answer N of M" preambles or directional instructions
             ("ענו על 5 מתוך 8 שאלות", "Answer 5 of the following 8"),
           - generic exam-level instructions (calculator policy, allowed
             materials, time limit),
           - answer keys or solution sheets if any leak into the upload.

        2. For pages that DO host questions, identify question boundaries
           using these cues (in order of reliability):
           - explicit Hebrew markers: "שאלה 1", "שאלה 2", … on a fresh line,
           - Hebrew letter markers: "א.", "ב.", "ג." at line start
             (these are sub-questions when nested under a שאלה N parent —
             do NOT split sub-questions into separate segments),
           - English markers: "Question 1", "Question 2", "Problem 3",
           - bare numbered markers at line start: "1.", "1)", "(1)".

        3. A single question MAY span multiple consecutive OCR pages.
           Common pattern: question text + figure on one page, sub-parts
           on the next. When pages obviously belong to the same question
           (no new שאלה N marker, continuation of the same problem
           statement), emit one segment with the multi-page span.

        4. Two questions MAY share a page (rare in Bagrut math but
           legal for the schema). When this happens, emit two segments
           with the same start_page == end_page.

        5. Page numbers are 1-based and refer to the OCR page numbers in
           the user message — NOT the printed page number from the exam.

        6. question_label_or_null:
           - When you see an explicit marker, copy it VERBATIM
             (e.g. "שאלה 3", "Question 3", "3.").
           - When the boundary is inferred from layout but no marker is
             visible (rare), set it to null.

        7. confidence: 0..1. Use 0.95+ when an explicit marker is visible
           on a clean page; 0.7-0.85 when the boundary is inferred from
           layout cues; 0.5-0.6 when the layout is genuinely ambiguous.
           NEVER fabricate confidence — under-report rather than over-report.

        ## Output shape

        Always return via the `segment_bagrut_questions` tool. Empty
        `segments` array is valid and means "this PDF has no questions"
        (instructions-only document, blank pages, etc.).

        Page numbers in output MUST exist in the user message page list.
        Returning a page number that is not in the input is a contract
        violation and the caller will fall back to a per-page heuristic.
        """;

    /// <summary>
    /// Build the per-PDF user prompt. Each page is rendered as
    /// <c>--- PAGE {N} ---</c> followed by its raw OCR text (truncated to
    /// keep the prompt under the 200k context window for very long PDFs).
    /// LaTeX is included verbatim so equation-only pages still show signal
    /// to the segmenter.
    /// </summary>
    public static string BuildUserPrompt(
        string examCode,
        string pdfId,
        IReadOnlyList<ExtractedPage> pages,
        int perPageCharLimit = 4000)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (perPageCharLimit < 100) perPageCharLimit = 100;

        var sb = new StringBuilder(pages.Count * Math.Min(perPageCharLimit, 1024));
        sb.Append("Bagrut PDF segmentation request.\n");
        sb.Append("exam_code: ").Append(examCode).Append('\n');
        sb.Append("pdf_id: ").Append(pdfId).Append('\n');
        sb.Append("total_pages: ").Append(pages.Count.ToString(CultureInfo.InvariantCulture)).Append("\n\n");

        foreach (var page in pages)
        {
            sb.Append("--- PAGE ").Append(page.PageNumber.ToString(CultureInfo.InvariantCulture)).Append(" ---\n");

            var text = page.RawText ?? string.Empty;
            if (text.Length > perPageCharLimit)
            {
                text = text[..perPageCharLimit] + "\n[...truncated for prompt budget...]";
            }
            sb.Append(text);

            if (!string.IsNullOrWhiteSpace(page.ExtractedLatex))
            {
                sb.Append("\n[LaTeX extracted on this page]\n");
                var latex = page.ExtractedLatex!;
                if (latex.Length > perPageCharLimit)
                {
                    latex = latex[..perPageCharLimit] + "\n[...truncated for prompt budget...]";
                }
                sb.Append(latex);
            }

            sb.Append("\n\n");
        }

        sb.Append(
            "Return one segment per question via `segment_bagrut_questions`. "
            + "Empty `segments` is valid when no questions are present.");

        return sb.ToString();
    }
}
