// =============================================================================
// Cena Platform — DefaultAnthropicSegmenterInvoker
//
// Mirrors DefaultAnthropicConceptExtractionInvoker. Owns:
//   - the tool-use schema for `segment_bagrut_questions`,
//   - cache_control: ephemeral on the system block (per ADR-0026 §6 +
//     routing-config.yaml §6),
//   - the response → (toolInput, tokens) projection.
//
// The Anthropic SDK client cache lives in IAnthropicLlmRuntime — this
// invoker just borrows the client per call so a flaky-model trip on
// segmentation also gates concept-extraction (and vice versa).
// =============================================================================

using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Cena.Admin.Api.AiSettings;

namespace Cena.Admin.Api.Ingestion.Segmenter;

/// <summary>
/// Default <see cref="IAnthropicSegmenterInvoker"/> implementation backed by
/// the Anthropic SDK + the shared <see cref="IAnthropicLlmRuntime"/> client cache.
/// </summary>
public sealed class DefaultAnthropicSegmenterInvoker : IAnthropicSegmenterInvoker
{
    /// <summary>
    /// Output token cap. The tool-use response is a list of segment objects
    /// (start_page, end_page, question_label_or_null, confidence). 2048
    /// tokens accommodates the worst observed Bagrut PDF (~16 questions on
    /// 30+ pages) with comfortable headroom; routing-config.yaml ceils
    /// per-task max-tokens at the same value.
    /// </summary>
    private const int MaxOutputTokens = 2048;

    private readonly IAnthropicLlmRuntime _runtime;

    public DefaultAnthropicSegmenterInvoker(IAnthropicLlmRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public async Task<(IReadOnlyDictionary<string, JsonElement>? ToolInput, long InputTokens, long OutputTokens)>
        InvokeAsync(
            string apiKey,
            string modelId,
            string systemPrompt,
            string userPrompt,
            CancellationToken ct)
    {
        var client = _runtime.GetOrCreateClient(apiKey);

        // System block carries the static segmenter rubric with ephemeral
        // cache_control so per-call cost is dominated by the small per-PDF
        // user message. Most prompt bytes are the rubric (cacheable); the
        // user message is the page-by-page OCR text (per-call).
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
            Name = "segment_bagrut_questions",
            Description =
                "Identify question boundaries inside a Hebrew Bagrut math exam PDF. "
                + "Return a list of {start_page, end_page, question_label_or_null} "
                + "for the QUESTIONS only — exclude exam cover, instructions, "
                + "answer-key, and 'answer N of M' preamble pages.",
            InputSchema = SegmentToolSchema,
        };

        var createParams = new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = MaxOutputTokens,
            Temperature = 0.0f, // deterministic — boundaries are not creative
            System = systemBlocks,
            Messages = new List<MessageParam>
            {
                new MessageParam { Role = "user", Content = userPrompt }
            },
            Tools = new List<ToolUnion> { tool },
            ToolChoice = new ToolChoiceTool { Name = "segment_bagrut_questions" },
        };

        // SDK ships a sync .Create that internally awaits; wrap on a worker
        // thread so the cancellation token is honored and the call doesn't
        // block the calling synchronization context (mirrors the pattern in
        // DefaultAnthropicConceptExtractionInvoker + AnthropicConnectionProbe).
        var response = await Task.Run(() => client.Messages.Create(createParams), ct).ConfigureAwait(false);

        long inputTokens = response.Usage.InputTokens;
        long outputTokens = response.Usage.OutputTokens;

        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out var toolUse) && toolUse.Name == "segment_bagrut_questions")
                return (toolUse.Input, inputTokens, outputTokens);
        }

        return (null, inputTokens, outputTokens);
    }

    // ── Tool schema (closed-set discipline encoded in the schema) ────────
    //
    // The Anthropic SDK is strict about tool_use schemas: Anthropic accepts a
    // standard JSON Schema fragment and uses it to constrain the model's
    // output. Keeping `additionalProperties: false` and a small required-set
    // makes the response highly deterministic at temperature=0.

    private static readonly InputSchema SegmentToolSchema = InputSchema.FromRawUnchecked(
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {
              "type": "object",
              "properties": {
                "segments": {
                  "type": "array",
                  "description": "One entry per question. Empty array means the PDF has no questions (instructions-only document).",
                  "items": {
                    "type": "object",
                    "properties": {
                      "start_page": {
                        "type": "integer",
                        "minimum": 1,
                        "description": "1-based first OCR page that hosts this question."
                      },
                      "end_page": {
                        "type": "integer",
                        "minimum": 1,
                        "description": "1-based last OCR page that hosts this question. Equals start_page for single-page questions."
                      },
                      "question_label_or_null": {
                        "type": ["string", "null"],
                        "description": "Bagrut-side label exactly as it appears in the PDF (e.g. 'שאלה 3', 'Question 3', '3')."
                      },
                      "confidence": {
                        "type": "number",
                        "minimum": 0,
                        "maximum": 1,
                        "description": "Segmenter confidence in this boundary. Lower for ambiguous layouts."
                      }
                    },
                    "required": ["start_page", "end_page", "confidence"]
                  }
                }
              },
              "required": ["segments"]
            }
            """)!);
}
