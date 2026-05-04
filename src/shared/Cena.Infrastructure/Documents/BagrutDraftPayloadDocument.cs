// =============================================================================
// Cena Platform — BagrutDraftPayloadDocument
//
// Stores the actual extracted content for a Bagrut draft: prompt text,
// LaTeX, figure spec, source page, extraction confidence. Sibling to
// PipelineItemDocument (same Id) — PipelineItemDocument is the kanban
// tracking row, this is the payload the variant-generator reads back
// to seed AI generation.
//
// Kept as a separate document so:
//   - PipelineItemDocument stays focused on kanban concerns and doesn't
//     bloat with payloads from one specific source type.
//   - The variant-generator (and any future consumer of Bagrut payloads)
//     queries one row by Id.
// =============================================================================

namespace Cena.Infrastructure.Documents;

public sealed class BagrutDraftPayloadDocument
{
    /// <summary>Same id as the linked PipelineItemDocument (the "draftId").</summary>
    public string Id { get; set; } = "";

    public string ExamCode { get; set; } = "";
    public string SourcePdfId { get; set; } = "";
    public int SourcePage { get; set; }

    public string Prompt { get; set; } = "";
    public string? LatexContent { get; set; }
    public string? FigureSpecJson { get; set; }
    public double ExtractionConfidence { get; set; }

    /// <summary>
    /// Persisted flag set by the curator at upload time — signals that
    /// this draft was derived from a Ministry-published past Bagrut
    /// (independent of whether MinistrySubjectCode / MinistryQuestionPaperCode
    /// are filled, which separately drives the BagrutCorpusItemDocument
    /// side-effect for the ADR-0043 isomorph rejector). Filterable so
    /// curators can scope the kanban / variant-generation flow to
    /// Ministry-only or non-Ministry-only items.
    /// </summary>
    public bool IsMinistryReference { get; set; }

    public List<string> ReviewNotes { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    // ── ADR-0062 Phase 1.5 — OCR enhancement persistence ───────────────
    // The /enhance-text endpoint runs the (Prompt + LatexContent) blob
    // through Anthropic and writes the cleaned result back here so the
    // SPA renders the enhanced view immediately on refresh, without
    // re-firing the LLM call. Source of truth for "what the curator sees
    // by default"; the OcrEnhancementCacheDocument is a separate concern
    // (cross-draft hit reuse) keyed on the input hash.

    /// <summary>
    /// Anthropic-cleaned text (math wrapped in \(...\)/\[...\], paragraph
    /// breaks restored, [[FIGURE:p&lt;n&gt;]] markers inserted). Null when the
    /// draft has never been enhanced or the input has changed since the
    /// last enhance (detected via <see cref="EnhancedInputHash"/>).
    /// </summary>
    public string? EnhancedText { get; set; }

    /// <summary>UTC timestamp the enhanced text was computed.</summary>
    public DateTimeOffset? EnhancedAt { get; set; }

    /// <summary>
    /// Anthropic model id that produced <see cref="EnhancedText"/> (e.g.
    /// "claude-sonnet-4-6"). Surfaced in the SPA's "Enhanced via LLM
    /// (model)" badge.
    /// </summary>
    public string? EnhancedBy { get; set; }

    /// <summary>
    /// SHA-256 (lower-hex) of the input that produced <see cref="EnhancedText"/>.
    /// Lets a re-OCR of the source page silently invalidate the stale
    /// enhanced text — when the curator re-OCRs, the new (Prompt + LatexContent)
    /// hash differs from this stored hash and the SPA-side check
    /// (`enhancedInputHash !== currentInputHash`) hides the stale view.
    /// </summary>
    public string? EnhancedInputHash { get; set; }

    // ── 2026-05-04 — Single-call PDF→HTML rendering (t_1c57e7389cb4) ────
    // Curator-triggered POST /api/admin/ingestion/items/{id}/render-html
    // runs the source PDF through PdfToHtmlOpusExtractor (Anthropic Opus
    // 4.7, full PDF in one call → self-contained HTML with inline SVGs and
    // HTML-native math). Persisted here so the SPA can prefer the high-
    // fidelity HTML over the legacy Prompt + EnhancedText when present.

    /// <summary>
    /// 2026-05-04: full HTML rendering of the question, produced by
    /// PdfToHtmlOpusExtractor. Null until /render-html has been hit.
    /// SPA prefers this over the legacy Prompt + EnhancedText when present.
    /// </summary>
    public string? HtmlContent { get; set; }

    /// <summary>UTC timestamp the HTML rendering was computed.</summary>
    public DateTimeOffset? HtmlContentAt { get; set; }

    /// <summary>
    /// Anthropic model id that produced <see cref="HtmlContent"/> (e.g.
    /// "claude-opus-4-7"). Surfaced in the SPA's "Rendered via LLM (model)"
    /// badge so curators can see which model produced the view they are
    /// reviewing — load-bearing when comparing fidelity across model runs.
    /// </summary>
    public string? HtmlContentModel { get; set; }
}
