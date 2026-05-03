// =============================================================================
// Cena Platform — DefaultAnthropicConceptExtractionInvoker (ADR-0062 Phase 1)
//
// The real Anthropic-side glue for HybridConceptExtractor. Owns:
//   - the Anthropic SDK call (client cache, MaxRetries=0 → caller's
//     circuit breaker is the retry surface),
//   - the tool definition (closed-set schema for tag_question_concepts),
//   - the cache_control: ephemeral on the system block per ADR-0026 §6,
//   - the response → (toolInput, tokens) projection.
//
// Why split out: HybridConceptExtractor is sealed (DI singleton lifetime),
// and a `virtual` test seam on a sealed class breaks the build (CS0549).
// Splitting the SDK call into a small interface gives unit tests a
// substitution point that doesn't pull the real SDK into a test process.
// =============================================================================

using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;

namespace Cena.Admin.Api.Mastery.Extraction;

public sealed class DefaultAnthropicConceptExtractionInvoker : IAnthropicConceptExtractionInvoker
{
    // Anthropic client cache — keyed on plaintext key, refreshed when the
    // operator rotates the persisted cipher.
    private AnthropicClient? _anthropicClient;
    private string? _lastApiKey;
    private readonly object _clientLock = new();

    private const int MaxOutputTokens = 512;

    public async Task<(IReadOnlyDictionary<string, JsonElement>? ToolInput, long InputTokens, long OutputTokens)>
        InvokeAsync(
            string apiKey,
            string modelId,
            string systemPrompt,
            string userPrompt,
            CancellationToken ct)
    {
        var client = GetOrCreateClient(apiKey);

        // System block carries the closed-set catalog with ephemeral
        // cache_control so per-call cost is dominated by the small
        // user message after the first warm hit. Per ADR-0026 §6 +
        // routing-config §6 (cache_control on system block).
        var systemBlocks = new List<TextBlockParam>
        {
            new TextBlockParam
            {
                Text = systemPrompt,
                CacheControl = new CacheControlEphemeral()
            }
        };

        var tool = new Tool
        {
            Name = "tag_question_concepts",
            Description = "Identify the primary and supporting skills a question exercises, "
                          + "drawing exclusively from the closed-set catalog in the system instructions.",
            InputSchema = ConceptToolSchema,
        };

        var createParams = new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = MaxOutputTokens,
            Temperature = 0.0f,
            System = systemBlocks,
            Messages = new List<MessageParam>
            {
                new MessageParam { Role = "user", Content = userPrompt }
            },
            Tools = new List<ToolUnion> { tool },
            ToolChoice = new ToolChoiceTool { Name = "tag_question_concepts" },
        };

        // SDK ships a sync .Create that internally awaits; wrap on a
        // worker thread so the cancellation token is honored and the
        // call doesn't block the calling synchronization context
        // (mirrors the AnthropicConnectionProbe pattern).
        var response = await Task.Run(() => client.Messages.Create(createParams), ct).ConfigureAwait(false);

        long inputTokens = response.Usage.InputTokens;
        long outputTokens = response.Usage.OutputTokens;

        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out var toolUse) && toolUse.Name == "tag_question_concepts")
                return (toolUse.Input, inputTokens, outputTokens);
        }

        return (null, inputTokens, outputTokens);
    }

    private AnthropicClient GetOrCreateClient(string apiKey)
    {
        lock (_clientLock)
        {
            if (_anthropicClient is not null && _lastApiKey == apiKey)
                return _anthropicClient;

            _anthropicClient = new AnthropicClient(new ClientOptions
            {
                ApiKey = apiKey,
                MaxRetries = 0,
            });
            _lastApiKey = apiKey;
            return _anthropicClient;
        }
    }

    // ── Tool schema (closed-set discipline encoded in the schema) ────────

    private static readonly InputSchema ConceptToolSchema = InputSchema.FromRawUnchecked(
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {
              "type": "object",
              "properties": {
                "primary": {
                  "type": "object",
                  "properties": {
                    "skillCode": { "type": "string", "description": "Canonical SkillCode from the catalog. Must match verbatim." },
                    "rationale": { "type": "string", "description": "One short sentence explaining the pick." },
                    "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                  },
                  "required": ["skillCode", "rationale", "confidence"]
                },
                "supporting": {
                  "type": "array",
                  "maxItems": 4,
                  "items": {
                    "type": "object",
                    "properties": {
                      "skillCode": { "type": "string", "description": "Canonical SkillCode from the catalog. Must match verbatim." },
                      "rationale": { "type": "string", "description": "One short sentence explaining the pick." },
                      "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                    },
                    "required": ["skillCode", "rationale", "confidence"]
                  }
                }
              },
              "required": ["supporting"]
            }
            """)!);
}
