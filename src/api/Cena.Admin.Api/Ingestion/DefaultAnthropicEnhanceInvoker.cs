// =============================================================================
// Cena Platform — DefaultAnthropicEnhanceInvoker (ADR-0062 Phase 1.5)
//
// Default IAnthropicEnhanceInvoker implementation. Owns:
//   - the system block (cache_control: ephemeral so per-call cost is
//     dominated by the small per-page user message),
//   - the visual-aware user-message composition: when a page PNG is
//     supplied, the user content is a list of [ImageBlockParam,
//     TextBlockParam]; otherwise, the legacy plain-string user message
//     is used to stay byte-for-byte compatible with cached responses
//     keyed off the legacy shape.
//   - the response → (text, tokens) projection.
//
// The Anthropic SDK client cache + circuit breaker live in
// IAnthropicLlmRuntime — this invoker borrows the client per call so a
// flaky-model trip on one call gates every other Anthropic seam.
//
// Image-block discipline
// ----------------------
// Anthropic's content-block image format requires:
//   - source.type = "base64",
//   - source.media_type ∈ {image/jpeg, image/png, image/gif, image/webp},
//   - source.data = base64-encoded bytes (no data: prefix).
// We rely on the SDK's strongly-typed Base64ImageSource + ImageBlockParam
// + ImageBlockParamSource union — implicit conversions thread Base64
// ImageSource → ImageBlockParamSource → ImageBlockParam → ContentBlockParam
// → MessageParamContent (via List<ContentBlockParam>).
// =============================================================================

using Anthropic.Models.Messages;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Llm;

namespace Cena.Admin.Api.Ingestion;

/// <summary>
/// Default <see cref="IAnthropicEnhanceInvoker"/> implementation backed by
/// the Anthropic SDK + the shared <see cref="IAnthropicLlmRuntime"/> client
/// cache.
/// </summary>
public sealed class DefaultAnthropicEnhanceInvoker : IAnthropicEnhanceInvoker
{
    private readonly IAnthropicLlmRuntime _runtime;

    public DefaultAnthropicEnhanceInvoker(IAnthropicLlmRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    /// <summary>
    /// One-line directive appended to the OCR text when an image is also
    /// attached. Tells the model that the image is the LOAD-BEARING source
    /// for stacked-fraction / exponent layout — the OCR text from Poppler
    /// has already collapsed numerator/denominator alignment.
    /// </summary>
    internal const string VisualGroundTruthDirective =
        "\n\n[VISUAL GROUND TRUTH] The attached page PNG is the source of truth " +
        "for layout. Use it to validate stacked-fraction numerator/denominator " +
        "pairing, exponent placement, and figure positions whenever the OCR text " +
        "above is ambiguous.";

    public async Task<(string? Text, long InputTokens, long OutputTokens)> InvokeAsync(
        string apiKey,
        string modelId,
        string systemPrompt,
        string ocrText,
        ReadOnlyMemory<byte>? sourcePagePng,
        int maxTokens,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(ocrText);

        var client = _runtime.GetOrCreateClient(apiKey);

        // System block carries the static cleanup rubric with ephemeral
        // cache_control. The rubric is large + stable; the OCR text is the
        // small per-call surface. CacheControl on the system block is the
        // standard Anthropic prompt-caching pattern.
        var systemBlocks = new List<TextBlockParam>
        {
            new TextBlockParam
            {
                Text = systemPrompt,
                CacheControl = new CacheControlEphemeral(),
            },
        };

        // Compose the user message. Two shapes:
        //   - With image: List<ContentBlockParam> = [Image, Text].
        //     IMPORTANT: image block FIRST so the model treats the image
        //     as the primary stimulus and the OCR text as the candidate
        //     reconstruction to validate against it. Anthropic's prompt
        //     guide explicitly recommends image-before-text for visual
        //     reasoning tasks (which fraction-layout reconstruction is).
        //   - Without image: legacy plain-string user message, identical
        //     to the pre-vision implementation. Backwards-compatible so
        //     drafts ingested before SourcePdfId/SourcePage were captured
        //     still enhance correctly.
        MessageParam userMessage;
        if (sourcePagePng is { } pngBytes && !pngBytes.IsEmpty)
        {
            var imageBlock = new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    MediaType = MediaType.ImagePng,
                    Data = Convert.ToBase64String(pngBytes.Span),
                },
            };
            var textBlock = new TextBlockParam
            {
                Text = ocrText + VisualGroundTruthDirective,
            };
            var blocks = new List<ContentBlockParam>
            {
                imageBlock, // implicit -> ContentBlockParam
                textBlock,  // implicit -> ContentBlockParam
            };
            userMessage = new MessageParam
            {
                Role = "user",
                Content = blocks, // implicit List<ContentBlockParam> -> MessageParamContent
            };
        }
        else
        {
            userMessage = new MessageParam
            {
                Role = "user",
                Content = ocrText, // implicit string -> MessageParamContent
            };
        }

        // Temperature note (2026-05-04): Opus 4.7 rejects the `temperature`
        // parameter outright (Anthropic deprecated it for that model in
        // favour of internal reasoning controls). Sonnet 4.6 / Haiku 4.5
        // still accept it. Detect the model family at the call site rather
        // than carrying a static "supports_temperature" flag in
        // routing-config — the SDK contract is "omit the field entirely
        // on Opus", which `Temperature = null` produces under the SDK's
        // nullable float? property. The legacy temperature=0 stays for the
        // older models so deterministic OCR cleanup keeps its pin.
        var supportsTemperature = !modelId.StartsWith("claude-opus-4-7", StringComparison.Ordinal);

        var createParams = new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = maxTokens,
            Temperature = supportsTemperature ? 0.0f : null,
            System = systemBlocks,
            Messages = new List<MessageParam> { userMessage },
        };

        // SDK ships a sync .Create that internally awaits; wrap on a worker
        // thread so the cancellation token is honored and the call doesn't
        // block the calling synchronization context (mirrors the pattern in
        // DefaultAnthropicSegmenterInvoker + AnthropicConnectionProbe).
        var response = await Task.Run(
            () => client.Messages.Create(createParams), ct).ConfigureAwait(false);

        long inputTokens = response.Usage.InputTokens;
        long outputTokens = response.Usage.OutputTokens;

        string? text = null;
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var t))
            {
                text = t.Text;
                break;
            }
        }

        return (text, inputTokens, outputTokens);
    }
}
