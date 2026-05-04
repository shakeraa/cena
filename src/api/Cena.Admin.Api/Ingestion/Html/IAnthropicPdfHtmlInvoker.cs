// =============================================================================
// Cena Platform — IAnthropicPdfHtmlInvoker (2026-05-04, t_1c57e7389cb4)
//
// SDK-boundary test seam for PdfToHtmlOpusExtractor. Mirrors the
// IAnthropicEnhanceInvoker / IAnthropicSegmenterInvoker / IAnthropicConcept
// ExtractionInvoker pattern — production wiring uses
// DefaultAnthropicPdfHtmlInvoker which builds the actual MessageCreateParams
// (DocumentBlockParam with Base64PdfSource + ThinkingConfigAdaptive +
// OutputConfig.Effort=High); tests inject a CapturingPdfHtmlInvoker so we
// can assert the params shape without round-tripping to Anthropic.
//
// The invoker is purposely narrow: it owns ONLY the SDK call. Resolving the
// API key, model id, tracing, cost-metric emission, fence-stripping all live
// in PdfToHtmlOpusExtractor. The boundary mirrors how DefaultAnthropic
// EnhanceInvoker keeps the system block + image-block composition local but
// the breaker / cost meter live one layer up.
// =============================================================================

namespace Cena.Admin.Api.Ingestion.Html;

/// <summary>
/// Outbound Anthropic call for the PDF→HTML pass. Returns the raw text
/// produced by the model (including any markdown fences the model added
/// despite the system-prompt rule — caller strips them) plus token usage.
/// The Text may be null when Anthropic responded successfully but produced
/// no text block — caller treats that as an empty-response error.
/// </summary>
public interface IAnthropicPdfHtmlInvoker
{
    /// <summary>
    /// Call Anthropic with a (system + user) message pair. The user message
    /// carries TWO content blocks in this exact order:
    ///   1. DocumentBlockParam wrapping a Base64PdfSource of the full PDF
    ///      bytes (CacheControl Ttl5m so a curator who fires /render-html
    ///      twice in 5 minutes pays the prompt-cache discount).
    ///   2. TextBlockParam carrying the per-call instruction.
    ///
    /// On Opus 4.7 the request also enables adaptive extended thinking
    /// (ThinkingConfigAdaptive) and high-effort output config — this is the
    /// user-validated recipe from the task body. Sonnet 4.6 / Haiku rows
    /// drop the temperature param entirely (Opus 4.7 rejects it; the
    /// historic temperature pin lives in the resolved-model branch).
    /// </summary>
    /// <param name="apiKey">Anthropic API key (cipher-decrypted).</param>
    /// <param name="modelId">Model id resolved by IModelResolver.</param>
    /// <param name="systemPrompt">System block text (cacheable on the system row).</param>
    /// <param name="pdfBytes">Full PDF byte stream — sent as base64.</param>
    /// <param name="instruction">Per-call user instruction text.</param>
    /// <param name="maxTokens">Upper bound on output tokens.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<(string? Text, long InputTokens, long OutputTokens)> InvokeAsync(
        string apiKey,
        string modelId,
        string systemPrompt,
        ReadOnlyMemory<byte> pdfBytes,
        string instruction,
        int maxTokens,
        CancellationToken ct);
}
