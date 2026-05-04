// =============================================================================
// Cena Platform — IPdfToHtmlExtractor + DTOs (2026-05-04, t_1c57e7389cb4)
//
// Single-call PDF → self-contained HTML extractor. The user validated this
// recipe against claude.ai directly on corpus/tests/35581-q.pdf and got the
// gold-standard output (Hebrew RTL preserved, math as HTML sup/sub/fraction
// divs — NOT LaTeX, figures recreated as inline <svg>) on the first attempt.
//
// Replaces (does NOT remove) two earlier paths that fed broken intermediate
// representations to the LLM and got hallucinated stacked fractions back:
//   1. Poppler `pdftotext -layout` → Sonnet text-only enhance
//   2. Poppler text + page PNG → Sonnet enhance with vision-aware ground truth
//
// Both still ship and are still useful. /render-html is additive: a curator
// triggers it explicitly when they want the high-fidelity HTML view.
//
// Why the abstraction:
//   - Production wiring uses Anthropic Opus 4.7 with the full PDF as a
//     DocumentBlockParam. Tests need to verify the request shape (model,
//     system prompt content, params shape, fence-stripping) without spinning
//     up a real Anthropic call. The interface lets test scaffolding inject
//     a fake while keeping the production class sealed + DI-friendly.
//   - `ConvertAsync` MUST NOT throw on Anthropic errors — it returns
//     Success=false with a populated Error so the calling endpoint can map
//     to a structured 400 response. Same fail-loud-but-don't-throw pattern
//     as IOcrTextEnhancer / IBagrutQuestionSegmenter.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.Html;

/// <summary>
/// Request to render one Bagrut PDF into a self-contained HTML document.
/// </summary>
/// <param name="PdfBytes">Full PDF byte stream — Opus reads the whole document.</param>
/// <param name="PdfId">Content-hash id from <see cref="IBagrutPdfStore"/>; threaded through logs and traces.</param>
/// <param name="Instruction">
/// Optional override for the per-call user instruction. The default
/// (<see cref="DefaultInstruction"/>) tells the model to extract every
/// question with figures into a single HTML document. Curators can pass a
/// narrower instruction (e.g. "extract only question 1") in a follow-up
/// iteration; v1 always uses the default.
/// </param>
public sealed record PdfToHtmlRequest(
    byte[] PdfBytes,
    string PdfId,
    string? Instruction = null)
{
    /// <summary>
    /// Default per-call instruction. The system prompt does the heavy lifting
    /// (Hebrew RTL, math as HTML, inline SVGs, embedded style block); this
    /// instruction just tells the model what scope of the document to render.
    /// Adapted verbatim from the user's tested recipe.
    /// </summary>
    public const string DefaultInstruction =
        "Extract every question from this exam PDF, including all figures, into a single HTML document.";
}

/// <summary>
/// Response from a single PDF→HTML conversion. <c>Success=false</c> rows
/// carry the error reason in <see cref="Error"/> and are NEVER thrown to
/// the caller — the extractor catches every API/circuit failure so the
/// /render-html endpoint can map cleanly to a 400 with a structured
/// CenaError shape.
/// </summary>
public sealed record PdfToHtmlResponse(
    bool Success,
    string Html,
    string? ModelUsed,
    string? Error,
    long InputTokens,
    long OutputTokens);

/// <summary>
/// Single-call PDF → HTML extractor. Adapts the user's verbatim Opus 4.7
/// recipe into admin-api conventions (DI, ModelResolver, cost meter,
/// breaker, trace_id).
/// </summary>
public interface IPdfToHtmlExtractor
{
    /// <summary>
    /// Convert <paramref name="req"/> into a self-contained HTML document.
    /// Never throws — failures land in <see cref="PdfToHtmlResponse.Error"/>
    /// with <see cref="PdfToHtmlResponse.Success"/> = false.
    /// </summary>
    Task<PdfToHtmlResponse> ConvertAsync(PdfToHtmlRequest req, CancellationToken ct = default);
}
