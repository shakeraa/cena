// =============================================================================
// Cena Platform — GeminiVisionRunner (ADR-0033 Layer 4b)
//
// Real IGeminiVisionRunner. Calls Gemini 1.5 Flash (multimodal) with the
// cropped text region and a deterministic system prompt, returns a new
// OcrTextBlock with the recovered text.
//
// API:
//   POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={ApiKey}
//   Body:
//     { "contents": [ {
//         "parts": [
//           { "text": "<system prompt + any user instruction>" },
//           { "inline_data": { "mime_type": "image/png", "data": "<base64>" } }
//         ] } ],
//       "generationConfig": { "temperature": 0, "maxOutputTokens": 2048 } }
//   Response:
//     { "candidates": [ {
//         "content": { "parts": [ { "text": "<extracted>" } ] },
//         "finishReason": "STOP" } ] }
//
// Wiring (composition root):
//   services.Configure<GeminiVisionOptions>(cfg.GetSection("Ocr:Gemini"));
//   services.AddHttpClient<IGeminiVisionRunner, GeminiVisionRunner>()
//     .AddPolicyHandler(HttpPolicies.StandardRetryPolicy)
//     .AddPolicyHandler(HttpPolicies.CircuitBreakerPolicy);   // RDY-012
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class GeminiVisionRunner : IGeminiVisionRunner
{
    private readonly HttpClient _http;
    private readonly GeminiVisionOptions _options;
    private readonly ILogger<GeminiVisionRunner>? _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GeminiVisionRunner(
        HttpClient http,
        IOptions<GeminiVisionOptions> options,
        ILogger<GeminiVisionRunner>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _log = log;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "GeminiVisionOptions.ApiKey is required. Bind from the secure config " +
                "section \"Ocr:Gemini\" — never commit.");
        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("GeminiVisionOptions.Model is required.");

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl);
        if (_http.Timeout == Timeout.InfiniteTimeSpan ||
            _http.Timeout > _options.RequestTimeout)
        {
            _http.Timeout = _options.RequestTimeout;
        }
    }

    public async Task<OcrTextBlock> RescueTextAsync(
        ReadOnlyMemory<byte> croppedRegionBytes,
        OcrTextBlock originalBlock,
        CancellationToken ct)
    {
        if (croppedRegionBytes.IsEmpty)
            throw new ArgumentException("Crop must be non-empty.", nameof(croppedRegionBytes));
        if (croppedRegionBytes.Length > _options.MaxImageBytes)
            throw new ArgumentException(
                $"Crop exceeds MaxImageBytes={_options.MaxImageBytes:N0} bytes.", nameof(croppedRegionBytes));

        var sw = Stopwatch.StartNew();

        var payload = new GenerateContentRequest(
            Contents: new[]
            {
                new Content(Parts: new Part[]
                {
                    new Part(Text: _options.SystemPrompt),
                    new Part(InlineData: new InlineData(
                        MimeType: "image/png",
                        Data: Convert.ToBase64String(croppedRegionBytes.Span))),
                }),
            },
            GenerationConfig: new GenerationConfig(Temperature: 0.0, MaxOutputTokens: 2048));

        // The endpoint path is "models/{model}:generateContent?key={ApiKey}"
        var path = $"models/{Uri.EscapeDataString(_options.Model)}:generateContent?key={Uri.EscapeDataString(_options.ApiKey!)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(path, payload, JsonOpts, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            _log?.LogWarning("[OCR_CASCADE] Gemini timeout — signalling circuit-open");
            throw new OcrCircuitOpenException("Gemini request timed out.");
        }
        catch (HttpRequestException ex)
        {
            if (MathpixRunner.IsBrokenCircuitException(ex))
                throw new OcrCircuitOpenException("Gemini circuit breaker open.", ex);
            _log?.LogWarning(ex, "[OCR_CASCADE] Gemini HTTP transport failure");
            throw new OcrCircuitOpenException("Gemini transport failure.", ex);
        }
        catch (Exception ex) when (MathpixRunner.IsBrokenCircuitException(ex))
        {
            throw new OcrCircuitOpenException("Gemini circuit breaker open.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _log?.LogWarning(
                    "[OCR_CASCADE] Gemini returned {Status}",
                    (int)response.StatusCode);
                return originalBlock;
            }

            GenerateContentResponse? payloadResult;
            try
            {
                payloadResult = await response.Content
                    .ReadFromJsonAsync<GenerateContentResponse>(JsonOpts, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[OCR_CASCADE] Gemini response malformed");
                return originalBlock;
            }

            var extracted = ExtractText(payloadResult)?.Trim();
            if (string.IsNullOrEmpty(extracted))
            {
                _log?.LogDebug("[OCR_CASCADE] Gemini returned empty text; keeping original");
                return originalBlock;
            }

            // Gemini doesn't expose a per-token confidence. We assign a
            // conservative fixed confidence (0.90) to rescued text — high
            // enough to clear τ, low enough to signal "cloud fallback" in the
            // downstream confidence-weighted aggregate.
            sw.Stop();
            bool isRtl = ContainsHebrew(extracted);
            _log?.LogDebug(
                "[OCR_CASCADE] Gemini rescue ok chars={Chars} latencyMs={Ms}",
                extracted.Length, sw.Elapsed.TotalMilliseconds);

            return originalBlock with
            {
                Text = extracted,
                Confidence = 0.90,
                IsRtl = isRtl,
                Language = isRtl ? Language.Hebrew : originalBlock.Language,
            };
        }
    }

    private static string? ExtractText(GenerateContentResponse? r)
    {
        if (r?.Candidates is not { Length: > 0 }) return null;
        var parts = r.Candidates[0].Content?.Parts;
        if (parts is null) return null;
        // Concatenate all text parts (Gemini may return a few chunks).
        return string.Concat(parts.Select(p => p.Text ?? string.Empty));
    }

    private static bool ContainsHebrew(string text)
    {
        foreach (var c in text)
            if (c >= 0x0590 && c <= 0x05FF) return true;
        return false;
    }

    // -------------------------------------------------------------------------
    // Wire DTOs
    // -------------------------------------------------------------------------
    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("contents")]         Content[] Contents,
        [property: JsonPropertyName("generationConfig")] GenerationConfig? GenerationConfig);

    private sealed record Content(
        [property: JsonPropertyName("parts")] Part[] Parts,
        [property: JsonPropertyName("role")]  string? Role = null);

    private sealed record Part(
        [property: JsonPropertyName("text")]       string? Text = null,
        [property: JsonPropertyName("inlineData")] InlineData? InlineData = null);

    private sealed record InlineData(
        [property: JsonPropertyName("mimeType")] string MimeType,
        [property: JsonPropertyName("data")]     string Data);

    private sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")]     double? Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens);

    private sealed record GenerateContentResponse(
        [property: JsonPropertyName("candidates")] Candidate[]? Candidates);

    private sealed record Candidate(
        [property: JsonPropertyName("content")]      Content? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);
}
