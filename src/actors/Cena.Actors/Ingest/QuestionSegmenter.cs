// =============================================================================
// Cena Platform — Question Segmenter
// Segments OCR output into individual questions with sub-parts.
// Uses LLM-first approach: send full page text to Gemini for structured extraction.
// =============================================================================

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Ingest;

/// <summary>
/// A single question segmented from a document page.
/// </summary>
public sealed record SegmentedQuestion(
    int QuestionNumber,
    int? Points,
    string StemText,
    Dictionary<string, string> MathExpressions,
    List<SegmentedSubPart> SubParts,
    string? DiagramDescription,
    float Confidence);

public sealed record SegmentedSubPart(
    string PartId,
    string Text,
    List<string>? DependsOn);

public interface IQuestionSegmenter
{
    Task<List<SegmentedQuestion>> SegmentAsync(OcrPageOutput ocrPage, CancellationToken ct = default);
}

public sealed class GeminiQuestionSegmenter : IQuestionSegmenter
{
    private readonly HttpClient _http;
    private readonly GeminiOcrOptions _options;
    private readonly ILogger<GeminiQuestionSegmenter> _logger;

    private const string SegmentationPrompt = """
        You are segmenting an Israeli Bagrut exam page into individual questions.

        Rules:
        - Detect question numbers (1, 2, 3...) and sub-parts (א, ב, ג or a, b, c)
        - Extract point allocation if present (e.g., "25 נקודות")
        - Preserve Hebrew/Arabic text exactly
        - Math expressions should be LaTeX. Use {math:key} placeholders in text.
        - Mark sub-part dependencies: if part ב depends on the answer from א, note it
        - Describe any diagrams/figures briefly

        Return JSON array:
        [
          {
            "question_number": 1,
            "points": 25,
            "stem_text": "נתונה הפונקציה {math:f}",
            "math_expressions": { "f": "f(x) = x^3 - 3x^2 + 4" },
            "sub_parts": [
              { "part_id": "a", "text": "מצא את נקודות הקיצון.", "depends_on": null },
              { "part_id": "b", "text": "מצא את תחומי העלייה.", "depends_on": ["a"] }
            ],
            "diagram_description": null,
            "confidence": 0.92
          }
        ]
        """;

    public GeminiQuestionSegmenter(
        HttpClient http,
        IOptions<GeminiOcrOptions> options,
        ILogger<GeminiQuestionSegmenter> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<SegmentedQuestion>> SegmentAsync(OcrPageOutput ocrPage, CancellationToken ct = default)
    {
        var prompt = $"{SegmentationPrompt}\n\nPage text:\n{ocrPage.RawText}";

        var request = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new { temperature = 0.1f, responseMimeType = "application/json" }
        };

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        var response = await _http.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var geminiResponse = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var text = geminiResponse
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrEmpty(text))
            return new();

        return ParseSegmentedQuestions(text);
    }

    private List<SegmentedQuestion> ParseSegmentedQuestions(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<SegmentedQuestionJson>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return items?.Select(i => new SegmentedQuestion(
                QuestionNumber: i.QuestionNumber,
                Points: i.Points,
                StemText: i.StemText ?? "",
                MathExpressions: i.MathExpressions ?? new(),
                SubParts: i.SubParts?.Select(sp => new SegmentedSubPart(
                    sp.PartId ?? "", sp.Text ?? "", sp.DependsOn)).ToList() ?? new(),
                DiagramDescription: i.DiagramDescription,
                Confidence: i.Confidence
            )).ToList() ?? new();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse segmented questions JSON");
            return new();
        }
    }

    private sealed class SegmentedQuestionJson
    {
        [JsonPropertyName("question_number")]
        public int QuestionNumber { get; set; }

        [JsonPropertyName("points")]
        public int? Points { get; set; }

        [JsonPropertyName("stem_text")]
        public string? StemText { get; set; }

        [JsonPropertyName("math_expressions")]
        public Dictionary<string, string>? MathExpressions { get; set; }

        [JsonPropertyName("sub_parts")]
        public List<SubPartJson>? SubParts { get; set; }

        [JsonPropertyName("diagram_description")]
        public string? DiagramDescription { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }

    private sealed class SubPartJson
    {
        [JsonPropertyName("part_id")]
        public string? PartId { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("depends_on")]
        public List<string>? DependsOn { get; set; }
    }
}
