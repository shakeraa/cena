// =============================================================================
// Cena Platform — AI Content Safety Client (PP-001 Stage 2)
// Azure AI Content Safety (or equivalent) for image/text classification.
// =============================================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Moderation;

public interface IContentSafetyClient
{
    bool IsConfigured { get; }
    Task<double> ClassifyAsync(byte[] content, string contentType, CancellationToken ct = default);
}

public sealed class ContentSafetyOptions
{
    public const string Section = "Moderation:ContentSafety";
    public string? ApiKey { get; set; }
    public string Endpoint { get; set; } = "https://eastus.api.cognitive.microsoft.com/contentsafety/image:analyze?api-version=2024-09-01";
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// HTTP client for Azure AI Content Safety.
/// Returns a safety score [0.0, 1.0] where 1.0 = fully safe.
/// On failure, returns a low score (0.50) to route to human review,
/// rather than auto-approving unscanned content.
/// </summary>
public sealed class ContentSafetyClient : IContentSafetyClient
{
    private readonly HttpClient _http;
    private readonly ContentSafetyOptions _options;
    private readonly ILogger<ContentSafetyClient> _logger;

    public ContentSafetyClient(
        HttpClient http,
        IOptions<ContentSafetyOptions> options,
        ILogger<ContentSafetyClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.ApiKey);

    public async Task<double> ClassifyAsync(byte[] content, string contentType, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Content Safety API key not configured — returning NeedsReview score");
            return 0.50; // Routes to human review, not auto-approve
        }

        var base64 = Convert.ToBase64String(content);
        var payload = JsonSerializer.Serialize(new
        {
            image = new { content = base64 },
            categories = new[] { "Hate", "SelfHarm", "Sexual", "Violence" }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Content Safety API request failed — routing to human review");
            return 0.50;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Content Safety API returned {StatusCode} — routing to human review",
                response.StatusCode);
            return 0.50;
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Azure returns categoriesAnalysis with severity 0-6 per category.
            // Convert to a 0.0-1.0 safety score: 1.0 - (maxSeverity / 6.0)
            double maxSeverity = 0;
            if (root.TryGetProperty("categoriesAnalysis", out var categories))
            {
                foreach (var cat in categories.EnumerateArray())
                {
                    if (cat.TryGetProperty("severity", out var sev))
                    {
                        var s = sev.GetDouble();
                        if (s > maxSeverity) maxSeverity = s;
                    }
                }
            }

            var score = 1.0 - (maxSeverity / 6.0);
            return Math.Clamp(score, 0.0, 1.0);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Content Safety response — routing to human review");
            return 0.50;
        }
    }
}
