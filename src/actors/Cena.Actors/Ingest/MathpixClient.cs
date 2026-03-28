// =============================================================================
// Cena Platform — Mathpix OCR Client (Fallback)
// Specialist math-to-LaTeX OCR. Used when Gemini confidence < threshold.
// =============================================================================

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Ingest;

public sealed class MathpixOptions
{
    public string AppId { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.mathpix.com/v3";
}

public sealed class MathpixClient : IMathOcrClient
{
    private readonly HttpClient _http;
    private readonly MathpixOptions _options;
    private readonly ILogger<MathpixClient> _logger;

    public string ProviderName => "mathpix";

    public MathpixClient(
        HttpClient http,
        IOptions<MathpixOptions> options,
        ILogger<MathpixClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ExtractLatexAsync(Stream imageStream, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, ct);
        var base64 = Convert.ToBase64String(ms.ToArray());

        var request = new MathpixRequest
        {
            Src = $"data:image/png;base64,{base64}",
            Formats = new[] { "latex_styled" },
            MathInlineDelimiters = new[] { "$", "$" },
            MathDisplayDelimiters = new[] { "$$", "$$" }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/text");
        httpRequest.Headers.Add("app_id", _options.AppId);
        httpRequest.Headers.Add("app_key", _options.AppKey);
        httpRequest.Content = JsonContent.Create(request);

        var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MathpixResponse>(cancellationToken: ct);
        return result?.LatexStyled ?? result?.Text ?? "";
    }

    private sealed class MathpixRequest
    {
        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("formats")]
        public string[] Formats { get; set; } = Array.Empty<string>();

        [JsonPropertyName("math_inline_delimiters")]
        public string[] MathInlineDelimiters { get; set; } = Array.Empty<string>();

        [JsonPropertyName("math_display_delimiters")]
        public string[] MathDisplayDelimiters { get; set; } = Array.Empty<string>();
    }

    private sealed class MathpixResponse
    {
        [JsonPropertyName("latex_styled")]
        public string? LatexStyled { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }
}
