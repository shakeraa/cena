// =============================================================================
// Cena Platform — Gemini 2.5 Flash OCR Client
// Primary OCR: sends page images to Gemini with structured JSON output prompt.
// Handles Hebrew/Arabic RTL text + LTR math notation.
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Ingest;

public sealed class GeminiOcrOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public float MinConfidenceForFallback { get; set; } = 0.7f;
}

public sealed class GeminiOcrClient : IOcrClient
{
    private readonly HttpClient _http;
    private readonly GeminiOcrOptions _options;
    private readonly ILogger<GeminiOcrClient> _logger;

    public string ProviderName => "gemini-2.5-flash";

    private const string ExtractionPrompt = """
        You are an OCR specialist for Israeli Bagrut exam papers.
        Extract all text and mathematical expressions from this page.

        Rules:
        - Preserve Hebrew/Arabic text exactly as written (RTL)
        - Extract ALL mathematical expressions as LaTeX strings
        - For each math expression, assign a key like "eq_1", "eq_2", etc.
        - In the text, replace each math expression with {math:eq_N}
        - Detect question numbers, point allocations, and sub-parts (a, b, c)
        - Note if any diagrams/figures are present (describe briefly)

        Return valid JSON with this exact structure:
        {
          "language": "he" or "ar" or "en",
          "confidence": 0.0-1.0,
          "text_blocks": [
            { "text": "...", "is_math": false, "confidence": 0.95 },
            { "text": "f(x) = x^2", "is_math": true, "confidence": 0.92 }
          ],
          "math_expressions": { "eq_1": "f(x) = x^2", "eq_2": "\\frac{d}{dx}" },
          "structured_text": "full text with {math:eq_1} placeholders"
        }
        """;

    public GeminiOcrClient(
        HttpClient http,
        IOptions<GeminiOcrOptions> options,
        ILogger<GeminiOcrClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OcrPageOutput> ProcessPageAsync(
        Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var imageBytes = await ReadStreamAsync(imageStream, ct);
        var base64 = Convert.ToBase64String(imageBytes);

        var mimeType = contentType switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/webp" => "image/webp",
            "image/tiff" => "image/tiff",
            _ => "image/jpeg"
        };

        var request = new GeminiRequest
        {
            Contents = new[]
            {
                new GeminiContent
                {
                    Parts = new GeminiPart[]
                    {
                        new GeminiTextPart { Text = ExtractionPrompt },
                        new GeminiInlineDataPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = mimeType,
                                Data = base64
                            }
                        }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.1f,
                ResponseMimeType = "application/json"
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}";

        var response = await _http.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var text = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("Gemini returned empty OCR result");
            return new OcrPageOutput(1, "", "unknown", new(), 0f, new());
        }

        return ParseGeminiOcrOutput(text);
    }

    public async Task<OcrDocumentOutput> ProcessDocumentAsync(Stream pdfStream, CancellationToken ct = default)
    {
        // For PDFs, Gemini can process directly via inline data
        var pdfBytes = await ReadStreamAsync(pdfStream, ct);
        var base64 = Convert.ToBase64String(pdfBytes);

        var request = new GeminiRequest
        {
            Contents = new[]
            {
                new GeminiContent
                {
                    Parts = new GeminiPart[]
                    {
                        new GeminiTextPart { Text = ExtractionPrompt + "\n\nProcess ALL pages of this PDF. Return a JSON array of page results." },
                        new GeminiInlineDataPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = "application/pdf",
                                Data = base64
                            }
                        }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.1f,
                ResponseMimeType = "application/json"
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        var response = await _http.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var text = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("Gemini returned empty document OCR result");
            return new OcrDocumentOutput(new(), "unknown", 0f, 0, 0m);
        }

        return ParseGeminiDocumentOutput(text);
    }

    private OcrPageOutput ParseGeminiOcrOutput(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<GeminiOcrResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result is null)
                return new OcrPageOutput(1, "", "unknown", new(), 0f, new());

            var textBlocks = result.TextBlocks?.Select(b =>
                new OcrTextBlock(b.Text, null, b.Confidence, b.IsMath)).ToList() ?? new();

            return new OcrPageOutput(
                PageNumber: 1,
                RawText: result.StructuredText ?? "",
                DetectedLanguage: result.Language ?? "unknown",
                MathExpressions: result.MathExpressions ?? new(),
                Confidence: result.Confidence,
                TextBlocks: textBlocks);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini OCR JSON, returning raw text");
            return new OcrPageOutput(1, json, "unknown", new(), 0.3f, new());
        }
    }

    private OcrDocumentOutput ParseGeminiDocumentOutput(string json)
    {
        try
        {
            // Try parsing as array of page results
            var pages = JsonSerializer.Deserialize<List<GeminiOcrResult>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (pages is null || pages.Count == 0)
            {
                // Try as single result
                var single = ParseGeminiOcrOutput(json);
                return new OcrDocumentOutput(
                    Pages: new List<OcrPageOutput> { single },
                    DetectedLanguage: single.DetectedLanguage,
                    OverallConfidence: single.Confidence,
                    PageCount: 1,
                    EstimatedCostUsd: 0.001m);
            }

            var ocrPages = pages.Select((p, i) => new OcrPageOutput(
                PageNumber: i + 1,
                RawText: p.StructuredText ?? "",
                DetectedLanguage: p.Language ?? "unknown",
                MathExpressions: p.MathExpressions ?? new(),
                Confidence: p.Confidence,
                TextBlocks: p.TextBlocks?.Select(b =>
                    new OcrTextBlock(b.Text, null, b.Confidence, b.IsMath)).ToList() ?? new()
            )).ToList();

            var avgConfidence = ocrPages.Average(p => p.Confidence);
            var language = ocrPages.FirstOrDefault()?.DetectedLanguage ?? "unknown";

            return new OcrDocumentOutput(
                Pages: ocrPages,
                DetectedLanguage: language,
                OverallConfidence: avgConfidence,
                PageCount: ocrPages.Count,
                EstimatedCostUsd: ocrPages.Count * 0.0003m);
        }
        catch (JsonException)
        {
            var fallback = ParseGeminiOcrOutput(json);
            return new OcrDocumentOutput(
                new List<OcrPageOutput> { fallback },
                fallback.DetectedLanguage, fallback.Confidence, 1, 0.001m);
        }
    }

    private static async Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    // ── Gemini API Request/Response Models ──

    private sealed class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    [JsonPolymorphic]
    private abstract class GeminiPart { }

    private sealed class GeminiTextPart : GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    private sealed class GeminiInlineDataPart : GeminiPart
    {
        [JsonPropertyName("inline_data")]
        public GeminiInlineData InlineData { get; set; } = new();
    }

    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "";

        [JsonPropertyName("data")]
        public string Data { get; set; } = "";
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("responseMimeType")]
        public string? ResponseMimeType { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiResponseContent? Content { get; set; }
    }

    private sealed class GeminiResponseContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiResponsePart>? Parts { get; set; }
    }

    private sealed class GeminiResponsePart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    // ── Gemini OCR Output Parsing Models ──

    private sealed class GeminiOcrResult
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("text_blocks")]
        public List<GeminiTextBlockResult>? TextBlocks { get; set; }

        [JsonPropertyName("math_expressions")]
        public Dictionary<string, string>? MathExpressions { get; set; }

        [JsonPropertyName("structured_text")]
        public string? StructuredText { get; set; }
    }

    private sealed class GeminiTextBlockResult
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("is_math")]
        public bool IsMath { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }
}
