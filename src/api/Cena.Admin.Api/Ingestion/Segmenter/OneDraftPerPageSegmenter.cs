// =============================================================================
// Cena Platform — OneDraftPerPageSegmenter (fallback path)
//
// Pre-LLM behaviour preserved verbatim: every populated page (RawText OR
// ExtractedLatex non-empty) becomes a one-page segment with confidence
// inherited from the OCR cascade. Pages with no text and no math are SKIPPED
// (the prior inline implementation did the same).
//
// Two consumers:
//   1. The default segmenter when the LLM tier feature flag is OFF.
//   2. The fallback the LLM-tier segmenter delegates to when its outbound
//      Anthropic call fails for any reason (no key, breaker open, throw,
//      malformed response, segment references a page that does not exist).
//
// The implementation is intentionally trivial (one foreach, no I/O) so the
// LLM tier's fallback path stays fast — when Haiku times out the curator
// should still see an ingestion result within the same wall-clock window
// the legacy code path produced.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.Segmenter;

/// <summary>
/// Default fallback segmenter. Emits one segment per populated OCR page,
/// label=null, confidence inherited from the page's OCR confidence.
/// </summary>
public sealed class OneDraftPerPageSegmenter : IBagrutQuestionSegmenter
{
    /// <summary>
    /// Synchronous core — exposed so <see cref="LlmBagrutQuestionSegmenter"/>
    /// can call it without paying the Task allocation when degrading on the
    /// hot path. The async <see cref="SegmentAsync"/> wraps this in a
    /// completed Task to honour the interface.
    /// </summary>
    public IReadOnlyList<BagrutQuestionSegment> Segment(IReadOnlyList<ExtractedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var segments = new List<BagrutQuestionSegment>(pages.Count);
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.RawText)
                && string.IsNullOrWhiteSpace(page.ExtractedLatex))
            {
                continue; // skip empty pages — matches prior inline behaviour
            }

            segments.Add(new BagrutQuestionSegment(
                StartPage: page.PageNumber,
                EndPage: page.PageNumber,
                QuestionLabel: null,
                Confidence: page.OcrConfidence));
        }

        return segments;
    }

    public Task<IReadOnlyList<BagrutQuestionSegment>> SegmentAsync(
        IReadOnlyList<ExtractedPage> pages,
        string examCode,
        string pdfId,
        CancellationToken ct = default)
    {
        // examCode and pdfId are not used by the fallback (the heuristic is
        // local to the page text). They live on the contract so the LLM tier
        // can tag its trace_id on the outbound call.
        return Task.FromResult(Segment(pages));
    }
}
