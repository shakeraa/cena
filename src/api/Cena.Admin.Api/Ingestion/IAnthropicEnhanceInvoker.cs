// =============================================================================
// Cena Platform — Anthropic enhance invoker (test seam, ADR-0062 Phase 1.5)
//
// Why this exists
// ---------------
// OcrTextEnhancer used to call client.Messages.Create(...) inline. With the
// vision-aware extension (page PNG attached as an image content block) we
// need a substitution point so unit tests can:
//   (1) verify that, when SourcePagePng IS supplied, the outbound user
//       Content[] is a list of [ImageBlockParam, TextBlockParam] — not a
//       plain string;
//   (2) verify that, when SourcePagePng is NOT supplied, the outbound user
//       Content[] is the legacy text-only string (backwards-compatible).
//
// We could not introduce a `virtual` test seam on OcrTextEnhancer because
// it is sealed; mirrors the IAnthropicSegmenterInvoker / IAnthropicConcept
// ExtractionInvoker pattern already used for the segmenter and the concept
// extractor.
//
// Production wiring registers DefaultAnthropicEnhanceInvoker as the
// IAnthropicEnhanceInvoker; tests inject a hand-rolled fake.
// =============================================================================

namespace Cena.Admin.Api.Ingestion;

/// <summary>
/// Outbound Anthropic call for the OCR enhance pass. Returns the cleaned
/// text + token usage. The returned Text may be null when Anthropic
/// responded successfully but did NOT produce any text block — caller treats
/// that as an empty-response error.
/// </summary>
public interface IAnthropicEnhanceInvoker
{
    /// <summary>
    /// Call Anthropic with a (system + user) message pair. When
    /// <paramref name="sourcePagePng"/> is non-null, the user message is
    /// composed as a list of content blocks: an image block carrying the
    /// PNG bytes (base64) and a text block carrying <paramref name="ocrText"/>
    /// plus a one-line directive instructing the model to use the image as
    /// ground truth. When null, the legacy text-only user message is sent
    /// for backwards-compatibility (older drafts have no SourcePdfId yet,
    /// and a rasterize failure must not break the enhance call).
    /// </summary>
    /// <param name="apiKey">Anthropic API key (cipher-decrypted).</param>
    /// <param name="modelId">Model id resolved by IModelResolver.</param>
    /// <param name="systemPrompt">System block text (cacheable).</param>
    /// <param name="ocrText">Per-call OCR text from Poppler.</param>
    /// <param name="sourcePagePng">Optional source-page PNG bytes — when
    /// supplied, the model receives the image alongside the text so it can
    /// validate stacked-fraction layout.</param>
    /// <param name="maxTokens">Upper bound on output tokens.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<(string? Text, long InputTokens, long OutputTokens)> InvokeAsync(
        string apiKey,
        string modelId,
        string systemPrompt,
        string ocrText,
        ReadOnlyMemory<byte>? sourcePagePng,
        int maxTokens,
        CancellationToken ct);
}
