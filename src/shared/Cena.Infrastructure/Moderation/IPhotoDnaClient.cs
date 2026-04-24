// =============================================================================
// Cena Platform — PhotoDNA Client Interface + Implementation (PP-001)
// CSAM hash detection via Microsoft PhotoDNA Cloud Service.
// Fail-closed: if the service is unreachable, uploads are BLOCKED.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Moderation;

public record PhotoDnaMatch(bool IsMatch, string? MatchId, string? HashValue);

public interface IPhotoDnaClient
{
    bool IsConfigured { get; }
    Task<PhotoDnaMatch> CheckHashAsync(byte[] imageContent, CancellationToken ct = default);
}

public sealed class PhotoDnaOptions
{
    public const string Section = "Moderation:PhotoDna";
    public string? ApiKey { get; set; }
    public string Endpoint { get; set; } = "https://api.microsoftmoderator.com/photodna/v1.0/Match";
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// HTTP client for Microsoft PhotoDNA Cloud Service.
/// Computes perceptual hash match against the known CSAM database.
/// FAIL-CLOSED: any error (timeout, 5xx, network) returns IsMatch=true
/// semantics via the caller's circuit breaker, blocking the upload.
/// </summary>
public sealed class PhotoDnaClient : IPhotoDnaClient
{
    private readonly HttpClient _http;
    private readonly PhotoDnaOptions _options;
    private readonly ILogger<PhotoDnaClient> _logger;
    private readonly Counter<long> _checkCounter;

    public PhotoDnaClient(
        HttpClient http,
        IOptions<PhotoDnaOptions> options,
        ILogger<PhotoDnaClient> logger,
        IMeterFactory meterFactory)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        var meter = meterFactory.Create("Cena.Moderation", "1.0.0");
        _checkCounter = meter.CreateCounter<long>(
            "cena.moderation.csam.checks.total",
            description: "Total CSAM hash checks via PhotoDNA");
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.ApiKey);

    public async Task<PhotoDnaMatch> CheckHashAsync(byte[] imageContent, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("PhotoDNA API key not configured — BLOCKING upload (fail-closed)");
            _checkCounter.Add(1, new KeyValuePair<string, object?>("result", "not_configured"));
            throw new PhotoDnaUnavailableException("PhotoDNA API key not configured");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);

        using var formContent = new MultipartFormDataContent();
        var imageStream = new ByteArrayContent(imageContent);
        imageStream.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        formContent.Add(imageStream, "image", "upload.jpg");
        request.Content = formContent;

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PhotoDNA request failed — BLOCKING upload (fail-closed)");
            _checkCounter.Add(1, new KeyValuePair<string, object?>("result", "error"));
            throw new PhotoDnaUnavailableException("PhotoDNA service unreachable", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PhotoDNA returned {StatusCode} — BLOCKING upload (fail-closed)",
                response.StatusCode);
            _checkCounter.Add(1, new KeyValuePair<string, object?>("result", "error"));
            throw new PhotoDnaUnavailableException($"PhotoDNA returned {response.StatusCode}");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<PhotoDnaResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result?.IsMatch == true)
        {
            _logger.LogCritical("PhotoDNA CSAM MATCH — ContentHash={Hash}, TrackingId={TrackingId}",
                result.ContentId, result.TrackingId);
            _checkCounter.Add(1, new KeyValuePair<string, object?>("result", "detected"));
            return new PhotoDnaMatch(true, result.TrackingId, result.ContentId);
        }

        _checkCounter.Add(1, new KeyValuePair<string, object?>("result", "clean"));
        return new PhotoDnaMatch(false, null, null);
    }

    private sealed record PhotoDnaResponse(
        bool IsMatch,
        string? ContentId,
        string? TrackingId,
        int? Status);
}

/// <summary>
/// Thrown when PhotoDNA is unreachable. The caller MUST treat this as
/// fail-closed: block the upload, do not let content through unscanned.
/// </summary>
public sealed class PhotoDnaUnavailableException : Exception
{
    public PhotoDnaUnavailableException(string message) : base(message) { }
    public PhotoDnaUnavailableException(string message, Exception inner) : base(message, inner) { }
}
