// =============================================================================
// Cena Platform — Bagrut question segmenter contract (ADR-0062 §LLM segmenter)
//
// Replaces the prior "one draft per page" boundary logic in
// BagrutPdfIngestionService.ExtractQuestions. The segmenter is a single point
// of policy: given the OCR cascade pages, decide how to slice them into
// question-shaped drafts so that:
//
//   - exam-cover, instructions, "answer N of M" preambles, and answer-key
//     pages are SKIPPED (never produce a draft),
//   - questions that span multiple pages produce ONE draft (the curator does
//     not have to merge two half-drafts),
//   - questions that share a page produce TWO drafts (rare in Bagrut math
//     but the contract allows it).
//
// Why a contract rather than an inline LLM call:
//   - the LlmBagrutQuestionSegmenter must be feature-flag gated and degrade
//     to OneDraftPerPageSegmenter on every failure mode (no API key, breaker
//     open, malformed response, exception, …) — fail-open by construction;
//   - unit tests want to drive segmentation behaviour without mocking the
//     OCR cascade or the Anthropic SDK at the same time.
//
// The segmenter owns ONLY the boundary decision. Materialising
// IngestionDraftQuestion records (sanitising LaTeX, building review notes,
// figure spec JSON) stays in BagrutPdfIngestionService — keeping the
// segmenter pure makes the LLM-side test surface tight.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.Segmenter;

/// <summary>
/// One contiguous range of OCR pages the segmenter believes hosts a single
/// question. Page numbers are 1-based and inclusive on both ends.
/// <para>
/// <see cref="QuestionLabel"/> is the Bagrut-side label when one is detected
/// (e.g. <c>"שאלה 3"</c>, <c>"Question 3"</c>, <c>"3"</c>). Null when the
/// segmenter cannot identify a label — the curator names the draft on review.
/// </para>
/// <para>
/// <see cref="Confidence"/> is a 0..1 score the segmenter exposes for
/// downstream gating (e.g. low-confidence segments could fan out to a curator
/// review queue with a flag). Today the only consumer is the warning surface
/// in BagrutPdfIngestionService.BuildWarnings.
/// </para>
/// <para>
/// <see cref="IntraPageIndex"/> is the 0-based slot of this segment within
/// its <see cref="StartPage"/> when text-layer extraction detected multiple
/// `שאלה N` markers on the same page (user-reported defect on 35581-q.pdf
/// page 2 which carries Q1 + Q2). Null when the segment is the only one on
/// its start page (the common case). The field is OPTIONAL — both the LLM
/// segmenter and the legacy OneDraftPerPageSegmenter leave it null. The
/// materialiser uses it ONLY to disambiguate draft ids when two segments
/// share a page.
/// </para>
/// </summary>
public sealed record BagrutQuestionSegment(
    int StartPage,
    int EndPage,
    string? QuestionLabel,
    double Confidence)
{
    /// <summary>
    /// 0-based intra-page slot when multiple segments share <see cref="StartPage"/>.
    /// Null when the segment is the only one on its start page (legacy path).
    /// </summary>
    public int? IntraPageIndex { get; init; }

    /// <summary>
    /// Throws when the segment is structurally invalid (wrong page order,
    /// non-positive page numbers, confidence outside 0..1). Called by the
    /// materialiser before producing a draft so a faulty segmenter is caught
    /// at the boundary rather than silently producing a draft with a bogus
    /// SourcePage.
    /// </summary>
    public void Validate()
    {
        if (StartPage <= 0)
            throw new ArgumentOutOfRangeException(nameof(StartPage), StartPage,
                "StartPage must be 1-based positive.");
        if (EndPage < StartPage)
            throw new ArgumentOutOfRangeException(nameof(EndPage), EndPage,
                $"EndPage ({EndPage}) must be >= StartPage ({StartPage}).");
        if (double.IsNaN(Confidence) || Confidence < 0.0 || Confidence > 1.0)
            throw new ArgumentOutOfRangeException(nameof(Confidence), Confidence,
                "Confidence must be a finite value in [0,1].");
        if (IntraPageIndex is int idx && idx < 0)
            throw new ArgumentOutOfRangeException(nameof(IntraPageIndex), idx,
                "IntraPageIndex must be non-negative when set.");
    }
}

/// <summary>
/// Boundary-detection contract for Bagrut PDF ingestion.
/// Implementations must NEVER throw on malformed input — they fall back to
/// a sensible default (OneDraftPerPageSegmenter) so ingestion is fail-open.
/// </summary>
public interface IBagrutQuestionSegmenter
{
    /// <summary>
    /// Slice the OCR pages into question segments. The result must:
    /// <list type="bullet">
    ///   <item><description>contain page numbers that exist in <paramref name="pages"/>
    ///     (i.e. references to non-existent pages indicate a faulty
    ///     segmenter and trigger fallback in the caller),</description></item>
    ///   <item><description>not overlap (a page is referenced by at most one
    ///     segment — the current cascade does not split a page into
    ///     multiple drafts; if that requirement appears later, this contract
    ///     evolves to <c>(int Page, int RegionIndex)</c> tuples),</description></item>
    ///   <item><description>be ordered by ascending start page.</description></item>
    /// </list>
    /// An empty result is allowed — it means "the segmenter believes this
    /// PDF has no questions" (e.g. instructions-only document).
    /// </summary>
    Task<IReadOnlyList<BagrutQuestionSegment>> SegmentAsync(
        IReadOnlyList<ExtractedPage> pages,
        string examCode,
        string pdfId,
        CancellationToken ct = default);
}
