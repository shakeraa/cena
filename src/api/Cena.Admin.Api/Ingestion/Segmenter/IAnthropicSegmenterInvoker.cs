// =============================================================================
// Cena Platform — Anthropic segmenter invoker (test seam, ADR-0062)
//
// Mirrors IAnthropicConceptExtractionInvoker:
//   - the LLM-tier segmenter is sealed (DI singleton lifetime);
//   - a `virtual` test seam on a sealed class would break the build (CS0549);
//   - splitting the SDK call into a small interface gives unit tests a
//     substitution point that doesn't pull the real Anthropic SDK into a
//     test process.
//
// Production wiring registers DefaultAnthropicSegmenterInvoker as the
// IAnthropicSegmenterInvoker; tests inject a hand-rolled fake.
// =============================================================================

using System.Text.Json;

namespace Cena.Admin.Api.Ingestion.Segmenter;

/// <summary>
/// Outbound Anthropic call for the Bagrut segmenter. Returns the parsed
/// tool-use input plus token usage. The returned dictionary may be null when
/// Anthropic responded successfully but did NOT produce a tool_use block —
/// the caller treats that as graceful degradation (fall back to the
/// one-draft-per-page segmenter).
/// </summary>
public interface IAnthropicSegmenterInvoker
{
    /// <summary>
    /// Call Anthropic with a tool-use call to <c>segment_bagrut_questions</c>.
    /// May throw on transport / SDK errors — the caller catches Exception and
    /// degrades to the fallback segmenter (fail-open).
    /// </summary>
    Task<(IReadOnlyDictionary<string, JsonElement>? ToolInput, long InputTokens, long OutputTokens)>
        InvokeAsync(
            string apiKey,
            string modelId,
            string systemPrompt,
            string userPrompt,
            CancellationToken ct);
}
