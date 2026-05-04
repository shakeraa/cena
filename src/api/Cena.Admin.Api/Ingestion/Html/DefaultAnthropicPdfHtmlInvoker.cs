// =============================================================================
// Cena Platform — DefaultAnthropicPdfHtmlInvoker (2026-05-04, t_1c57e7389cb4)
//
// Production-grade IAnthropicPdfHtmlInvoker. Owns the actual SDK shape:
//   - User message with TWO content blocks in this exact order:
//       [DocumentBlockParam(Base64PdfSource, CacheControl Ttl5m),
//        TextBlockParam(instruction)]
//   - On Opus 4.7: ThinkingConfigAdaptive + OutputConfig.Effort=High,
//     temperature dropped (the model rejects the param). On Sonnet/Haiku
//     fall-throughs (curator override): temperature pinned to 0.0 for
//     determinism, no thinking/effort fields (those are Opus-only knobs).
//
// Streaming (added 2026-05-04 after coordinator directive):
//   - 32K-token Opus outputs reliably hit the SDK's HTTP timeout when called
//     synchronously (`client.Messages.Create(...)`). Switched to
//     `client.Messages.CreateStreaming(...)` which returns an
//     IAsyncEnumerable<RawMessageStreamEvent> the loop accumulates as
//     chunks arrive — same total latency, no timeout risk.
//   - Stop-reason detection: when the stream emits a RawMessageDeltaEvent
//     with StopReason=MaxTokens we throw a clear "Output truncated at
//     {maxTokens} tokens" message so curators see truncation as a real
//     failure rather than silently rendering a partial document. Same for
//     StopReason=Refusal.
//   - Token usage is captured from RawMessageStartEvent (input tokens, set
//     once at stream start) and RawMessageDeltaEvent.Usage (output tokens,
//     accumulated throughout the stream — the final delta carries the full
//     count).
//
// All Anthropic SDK types referenced — ThinkingConfigAdaptive, OutputConfig,
// Effort, Base64PdfSource, DocumentBlockParam, CacheControlEphemeral with
// Ttl=Ttl5m, RawMessageStreamEvent, RawContentBlockDeltaEvent, TextDelta,
// RawMessageDeltaEvent, StopReason — are present in Anthropic .NET SDK
// 12.9.0 (verified via reflection probe on lib/net9.0/Anthropic.dll,
// 2026-05-04).
// =============================================================================

using System.Text;
using Anthropic.Models.Messages;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Llm;

namespace Cena.Admin.Api.Ingestion.Html;

public sealed class DefaultAnthropicPdfHtmlInvoker : IAnthropicPdfHtmlInvoker
{
    private readonly IAnthropicLlmRuntime _runtime;

    public DefaultAnthropicPdfHtmlInvoker(IAnthropicLlmRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public async Task<(string? Text, long InputTokens, long OutputTokens)> InvokeAsync(
        string apiKey,
        string modelId,
        string systemPrompt,
        ReadOnlyMemory<byte> pdfBytes,
        string instruction,
        int maxTokens,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        if (pdfBytes.IsEmpty) throw new ArgumentException("pdfBytes is required.", nameof(pdfBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        var client = _runtime.GetOrCreateClient(apiKey);

        // System block — cacheable, stable rubric. CacheControlEphemeral
        // with Ttl5m mirrors the OcrTextEnhancer's invoker shape (the
        // rubric is the large stable surface; the per-call PDF + text are
        // the small varying surface).
        var systemBlocks = new List<TextBlockParam>
        {
            new TextBlockParam
            {
                Text = systemPrompt,
                CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl5m },
            },
        };

        // User message — DocumentBlockParam first so the model treats the
        // PDF as the primary stimulus (matches the recipe order verbatim).
        // Base64-encoding the PDF bytes is the SDK's required shape for
        // Source.Data; the SDK does not accept raw byte arrays here.
        var pdfBase64 = Convert.ToBase64String(pdfBytes.Span);
        var docBlock = new DocumentBlockParam
        {
            Source = new Base64PdfSource { Data = pdfBase64 },
            // Per-block CacheControl: with Ttl5m a re-fire of /render-html
            // within five minutes hits the prompt-cache. The PDF is the
            // dominant token cost (~30k input tokens for a 6-page Bagrut),
            // so this is load-bearing for cost.
            CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl5m },
        };
        var textBlock = new TextBlockParam { Text = instruction };
        var blocks = new List<ContentBlockParam>
        {
            docBlock,  // implicit -> ContentBlockParam
            textBlock, // implicit -> ContentBlockParam
        };
        var userMessage = new MessageParam
        {
            Role = Role.User,
            Content = blocks, // implicit List<ContentBlockParam> -> MessageParamContent
        };

        // Opus 4.7 contract:
        //   - rejects the `temperature` parameter outright (Anthropic
        //     deprecated it in favour of internal reasoning controls).
        //     Detect by model-id prefix and omit (`Temperature = null`).
        //   - accepts ThinkingConfigAdaptive (extended thinking with
        //     adaptive budget) and OutputConfig.Effort = High (the user's
        //     validated recipe — produces the gold-standard output).
        // Sonnet/Haiku fall-throughs (curator-override path): keep
        // temperature=0 for determinism and skip Thinking/OutputConfig.
        var isOpus47 = modelId.StartsWith("claude-opus-4-7", StringComparison.Ordinal);

        // The Anthropic 12.9.0 SDK marks MessageCreateParams.Thinking and
        // .OutputConfig as init-only, so the Opus-only knobs must land in
        // the object initializer rather than via post-construction assign.
        // Opus 4.7 path enables ThinkingConfigAdaptive + Effort.High (the
        // user's validated recipe — load-bearing for gold-standard output);
        // Sonnet/Haiku fall-throughs leave both null so the SDK omits the
        // fields from the wire-format request entirely.
        var createParams = isOpus47
            ? new MessageCreateParams
            {
                Model = modelId,
                MaxTokens = maxTokens,
                Temperature = null, // Opus 4.7 rejects the param
                System = systemBlocks,
                Messages = new List<MessageParam> { userMessage },
                Thinking = new ThinkingConfigParam(new ThinkingConfigAdaptive(), element: null),
                OutputConfig = new OutputConfig { Effort = Effort.High },
            }
            : new MessageCreateParams
            {
                Model = modelId,
                MaxTokens = maxTokens,
                Temperature = 0.0, // deterministic on Sonnet/Haiku fall-throughs
                System = systemBlocks,
                Messages = new List<MessageParam> { userMessage },
            };

        // ── Streaming loop ────────────────────────────────────────────────
        // 32K-token Opus outputs reliably hit the synchronous Create() HTTP
        // timeout. Streaming pushes chunks as they arrive so the SDK can
        // accumulate the full output without a wall-clock cap.
        var sb = new StringBuilder();
        long inputTokens = 0;
        long outputTokens = 0;

        await foreach (var evt in client.Messages.CreateStreaming(createParams, ct)
            .ConfigureAwait(false))
        {
            if (evt.TryPickStart(out var startEvt))
            {
                // Initial usage block — input_tokens is the prompt-cache-aware
                // count for THIS call. Captured once and never re-read.
                inputTokens = startEvt.Message.Usage.InputTokens;
                outputTokens = startEvt.Message.Usage.OutputTokens; // typically 0 at start
                continue;
            }

            if (evt.TryPickContentBlockDelta(out var blockDelta))
            {
                // Append text deltas. Other delta variants (input_json,
                // citations, thinking, signature) are not part of the
                // PDF→HTML pass; we ignore them to keep the accumulated
                // string text-only.
                if (blockDelta.Delta.TryPickText(out var textDelta))
                {
                    sb.Append(textDelta.Text);
                }
                continue;
            }

            if (evt.TryPickDelta(out var msgDelta))
            {
                // The final message_delta event carries the resolved
                // stop_reason and the cumulative output token count. We
                // surface MaxTokens / Refusal as exceptions so the caller
                // does not silently render a truncated document.
                if (msgDelta.Usage is not null)
                {
                    // Output tokens are cumulative-final on the stream-end
                    // delta; latch the highest seen value so we report the
                    // SDK's authoritative count even if mid-stream deltas
                    // emit non-final values.
                    if (msgDelta.Usage.OutputTokens > outputTokens)
                        outputTokens = msgDelta.Usage.OutputTokens;
                    if (msgDelta.Usage.InputTokens is long midInput && midInput > inputTokens)
                        inputTokens = midInput;
                }

                // SDK marks Delta non-nullable in metadata but the C#
                // nullable annotation may report a possible-null reference;
                // defensive guard so a malformed stream doesn't NPE.
                var delta = msgDelta.Delta;
                if (delta is null) continue;
                var stopReasonRaw = delta.StopReason.Raw();
                if (stopReasonRaw is not null)
                {
                    // Compare via the typed enum (ApiEnum<TRaw,TEnum> has
                    // an op_Equality with TEnum). Raw() returning null
                    // means the stream-mid delta carried no stop_reason yet.
                    StopReason resolved = delta.StopReason;
                    if (resolved == StopReason.MaxTokens)
                    {
                        throw new InvalidOperationException(
                            $"Output truncated at {maxTokens} tokens. Re-render with a higher max_tokens, or split the source PDF.");
                    }
                    if (resolved == StopReason.Refusal)
                    {
                        throw new InvalidOperationException(
                            "Model refused to process the PDF. Inspect the source for content the safety filter would gate (rare on Ministry exam PDFs).");
                    }
                }
                continue;
            }

            // Other event kinds (start/stop content block boundaries, raw
            // message_stop) carry no payload we accumulate. Pass through.
        }

        var finalText = sb.Length == 0 ? null : sb.ToString();
        return (finalText, inputTokens, outputTokens);
    }
}
