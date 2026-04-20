// =============================================================================
// Cena Platform — Content Segmenter
// SAI-06: Segments OCR output into non-question content (definitions, theorems,
// worked examples, etc.) using LLM classification.
// Preserves LaTeX math expressions. Filters out low-confidence segments.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Cena.Actors.Gateway;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Ingest;

public interface IContentSegmenter
{
    Task<IReadOnlyList<ContentDocument>> SegmentAsync(
        string ocrText,
        string subject,
        string language,
        int pageNumber,
        string pipelineItemId,
        CancellationToken ct);
}

// ADR-0045: OCR-to-structured-JSON extraction over a full textbook page
// (4096 output tokens). Long-context structured extraction — tier 3. Shares
// the `knowledge_graph_extraction` routing row (Kimi K2.5 primary, Sonnet
// fallback) in contracts/llm/routing-config.yaml.
//
// prr-047: one-shot ingestion — each OCR'd page is unique, so a
// response-level cache would be ~0% hits. The ingest pipeline itself
// deduplicates on content hash before reaching this service, so the same
// page is never segmented twice.
// prr-046: finops cost-center "content-segmentation". Batch ingest path.
[TaskRouting("tier3", "knowledge_graph_extraction")]
[FeatureTag("content-segmentation")]
[AllowsUncachedLlm("Unique per OCR'd page; upstream ingest pipeline dedupes on content hash so the same page is never segmented twice.")]
public sealed class ContentSegmenter : IContentSegmenter
{
    private readonly ILlmClient _llm;
    private readonly ILogger<ContentSegmenter> _logger;
    private readonly ILlmCostMetric _costMetric;

    private const float MinConfidence = 0.5f;

    private const string SystemPrompt = """
        You are a content extraction specialist for educational textbooks.
        Given OCR text from a textbook page, extract non-question content segments.

        Classify each segment as one of:
        - Definition: formal definitions of terms/concepts
        - Theorem: mathematical theorems, lemmas, corollaries
        - WorkedExample: step-by-step solved examples
        - Explanation: conceptual explanations and descriptions
        - Narrative: introductory or contextual text
        - Formula: standalone mathematical formulas or identities
        - Diagram: descriptions or captions of diagrams/figures
        - Summary: chapter/section summaries and key takeaways

        Rules:
        - Do NOT extract exam questions or exercises — only educational content
        - Preserve LaTeX math expressions exactly as they appear (e.g. $f(x) = x^2$)
        - Preserve Hebrew/Arabic text exactly
        - Each segment should be a self-contained unit of educational content
        - Assign a confidence score (0.0 to 1.0) for classification accuracy

        Return a JSON array:
        [
          {
            "text": "The plain text content of the segment",
            "text_html": "<p>HTML-formatted content with <code>LaTeX</code></p>",
            "type": "Definition",
            "topic": "Derivatives",
            "confidence": 0.95
          }
        ]
        """;

    public ContentSegmenter(
        ILlmClient llm,
        ILogger<ContentSegmenter> logger,
        ILlmCostMetric costMetric)
    {
        _llm = llm;
        _logger = logger;
        _costMetric = costMetric;
    }

    public async Task<IReadOnlyList<ContentDocument>> SegmentAsync(
        string ocrText,
        string subject,
        string language,
        int pageNumber,
        string pipelineItemId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return [];

        var userPrompt = $"""
            Subject: {subject}
            Language: {language}
            Page: {pageNumber}

            OCR Text:
            {ocrText}
            """;

        try
        {
            var response = await _llm.CompleteAsync(new LlmRequest(
                ModelId: "content-segmenter",
                SystemPrompt: SystemPrompt,
                UserPrompt: userPrompt,
                MaxTokens: 4096,
                Temperature: 0.1f), ct);

            // prr-046: per-feature cost tag on success path.
            _costMetric.Record(
                feature: "content-segmentation",
                tier: "tier3",
                task: "knowledge_graph_extraction",
                modelId: response.ModelId,
                inputTokens: response.InputTokens,
                outputTokens: response.OutputTokens);

            var segments = ParseSegments(response.Content);
            var now = DateTimeOffset.UtcNow;

            return segments
                .Where(s => s.Confidence >= MinConfidence)
                .Select(s => new ContentDocument
                {
                    PipelineItemId = pipelineItemId,
                    Text = s.Text ?? "",
                    TextHtml = s.TextHtml ?? "",
                    Type = ParseContentType(s.Type),
                    Subject = subject,
                    Topic = s.Topic,
                    Language = language,
                    PageNumber = pageNumber,
                    Confidence = s.Confidence,
                    ExtractedAt = now
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Content segmentation failed for pipeline {PipelineItemId} page {Page}",
                pipelineItemId, pageNumber);
            return [];
        }
    }

    private List<ContentSegmentJson> ParseSegments(string json)
    {
        try
        {
            // Strip markdown code fences if the LLM wraps its response
            var trimmed = json.Trim();
            if (trimmed.StartsWith("```"))
            {
                var firstNewline = trimmed.IndexOf('\n');
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline > 0 && lastFence > firstNewline)
                    trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }

            return JsonSerializer.Deserialize<List<ContentSegmentJson>>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse content segments JSON");
            return [];
        }
    }

    private static ContentType ParseContentType(string? type) =>
        type switch
        {
            "Definition" => ContentType.Definition,
            "Theorem" => ContentType.Theorem,
            "WorkedExample" => ContentType.WorkedExample,
            "Explanation" => ContentType.Explanation,
            "Narrative" => ContentType.Narrative,
            "Formula" => ContentType.Formula,
            "Diagram" => ContentType.Diagram,
            "Summary" => ContentType.Summary,
            _ => ContentType.Explanation
        };

    private sealed class ContentSegmentJson
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("text_html")]
        public string? TextHtml { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("topic")]
        public string? Topic { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }
}
