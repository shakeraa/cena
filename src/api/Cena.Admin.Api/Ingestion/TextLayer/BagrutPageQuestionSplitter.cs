// =============================================================================
// Cena Platform — BagrutPageQuestionSplitter (text-layer-first ingestion)
//
// Splits a single page's RawText into one or more (questionNumber, sliceText)
// pairs based on the markers BagrutQuestionMarkerScanner finds. User-reported
// defect on 35581-q.pdf page 2: it contains TWO `שאלה N` markers (Q1 walking-
// speed + Q2 geometric series) but the legacy per-page segmenter produced
// only one draft. This splitter emits two slices for that page.
//
// Slice semantics:
//   - Each slice's text is everything FROM the marker (inclusive) to the
//     next marker on the page (exclusive), or to the end of the page text
//     for the last marker.
//   - Pre-marker text (header lines like "מתמטיקה, חורף תשפ"ו, מס' 35581
//     נספח" or "השאלות / ענו על חמש מן השאלות") is dropped — that material
//     is not part of any question and only confuses the curator.
//   - Single-marker pages return one slice equal to the WHOLE page text
//     (we don't strip the header on single-marker pages because the
//     header is short and the curator can edit it out at review).
//   - Zero-marker pages return one slice equal to the whole page text
//     marked with QuestionNumber=null. This matches the prior one-draft-
//     per-page behaviour for continuation pages (e.g. 35581-q.pdf page 4
//     where Q5 spans into the page but no fresh marker appears).
//     The orchestrator decides whether to emit such a page as its own
//     draft or merge it into the previous question's draft.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.TextLayer;

/// <summary>
/// One slice of a page, scoped to a single question marker.
/// </summary>
/// <param name="QuestionNumber">
/// Bagrut question number 1-9 when a marker is present. Null when the page
/// has no markers (unsegmented continuation/header page).
/// </param>
/// <param name="Text">The slice text.</param>
public sealed record PageQuestionSlice(int? QuestionNumber, string Text);

internal static class BagrutPageQuestionSplitter
{
    /// <summary>
    /// Split <paramref name="rawText"/> into per-marker slices. See file
    /// header for slice semantics.
    /// </summary>
    public static IReadOnlyList<PageQuestionSlice> Split(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return Array.Empty<PageQuestionSlice>();

        var hits = BagrutQuestionMarkerScanner.FindMarkers(rawText);
        if (hits.Count == 0)
        {
            // No marker — caller handles as continuation.
            return new[] { new PageQuestionSlice(null, rawText) };
        }

        if (hits.Count == 1)
        {
            // Single marker — keep the whole page text. The header is
            // short (one or two lines) and the curator can trim at review.
            return new[] { new PageQuestionSlice(hits[0].QuestionNumber, rawText) };
        }

        var slices = new List<PageQuestionSlice>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
        {
            // Slice at the START OF THE LINE containing the marker — not
            // the marker offset itself. Hebrew RTL Bagrut convention puts
            // the question number to the RIGHT of the question's first
            // line, which in logical (memory) order means the marker is
            // at the END of that line. Slicing at hits[i].Index would
            // drop the first sentence of the question. Walk back from
            // the marker to the previous newline (or 0) and start there.
            var start = StartOfLine(rawText, hits[i].Index);
            var end = (i + 1 < hits.Count) ? StartOfLine(rawText, hits[i + 1].Index) : rawText.Length;
            // Defensive: clamp.
            if (start < 0) start = 0;
            if (end > rawText.Length) end = rawText.Length;
            if (end <= start) continue;

            var slice = rawText.Substring(start, end - start);
            slices.Add(new PageQuestionSlice(hits[i].QuestionNumber, slice));
        }

        return slices;
    }

    /// <summary>
    /// Walk backwards from <paramref name="offset"/> to the start of its
    /// containing line (i.e. the index immediately after the previous
    /// '\n', or 0 if none). Mirrors String.LastIndexOf('\n', offset) + 1
    /// but tolerates offset==text.Length and avoids the quirky -1 case.
    /// </summary>
    private static int StartOfLine(string text, int offset)
    {
        if (offset <= 0) return 0;
        if (offset > text.Length) offset = text.Length;
        var nl = text.LastIndexOf('\n', offset - 1);
        return nl < 0 ? 0 : nl + 1;
    }
}
