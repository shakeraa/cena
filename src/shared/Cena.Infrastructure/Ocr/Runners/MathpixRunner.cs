// =============================================================================
// Cena Platform — MathpixRunner (ADR-0033 Layer 4a)
//
// Real IMathpixRunner. No mocks, no stubs. Calls Mathpix /v3/text with the
// cropped math region, returns a new OcrMathBlock with the Mathpix-recovered
// LaTeX and confidence.
//
// API contract (https://docs.mathpix.com/#api-v3):
//   POST https://api.mathpix.com/v3/text
//   Headers:
//     app_id:       <MathpixOptions.AppId>
//     app_key:      <MathpixOptions.AppKey>
//     Content-Type: application/json
//   Body:
//     { "src": "data:image/png;base64,<crop>",
//       "formats": ["latex_styled"],
//       "ocr": ["math","text"] }
//   Response:
//     { "latex_styled": "3x+5=14",
//       "confidence": 0.98,
//       "confidence_rate": 1.0,
//       "error": null }
//
// Caller wiring (composition root — NOT inside this class):
//
//   services.Configure<MathpixOptions>(cfg.GetSection("Ocr:Mathpix"));
//   services.AddHttpClient<IMathpixRunner, MathpixRunner>()
//     .AddPolicyHandler(HttpPolicies.StandardRetryPolicy)
//     .AddPolicyHandler(HttpPolicies.CircuitBreakerPolicy);  // RDY-012
//
// When the circuit breaker (the Polly handler) opens, the HttpClient throws
// Polly's BrokenCircuitException which we translate into OcrCircuitOpenException
// so Layer 4 can tolerate it. Same for timeouts.
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class MathpixRunner : IMathpixRunner
{
    private readonly HttpClient _http;
    private readonly MathpixOptions _options;
    private readonly ILogger<MathpixRunner>? _log;

    // Mathpix requires snake_case request/response fields.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MathpixRunner(
        HttpClient http,
        IOptions<MathpixOptions> options,
        ILogger<MathpixRunner>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _log = log;

        if (string.IsNullOrWhiteSpace(_options.AppId))
            throw new InvalidOperationException(
                "MathpixOptions.AppId is required. Bind from the secure config section " +
                "\"Ocr:Mathpix\" — credentials must NEVER be committed.");
        if (string.IsNullOrWhiteSpace(_options.AppKey))
            throw new InvalidOperationException(
                "MathpixOptions.AppKey is required.");

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.DefaultRequestHeaders.Remove("app_id");
        _http.DefaultRequestHeaders.Remove("app_key");
        _http.DefaultRequestHeaders.Add("app_id", _options.AppId);
        _http.DefaultRequestHeaders.Add("app_key", _options.AppKey);
        if (_http.Timeout == Timeout.InfiniteTimeSpan ||
            _http.Timeout > _options.RequestTimeout)
        {
            _http.Timeout = _options.RequestTimeout;
        }
    }

    public async Task<OcrMathBlock> RescueMathAsync(
        ReadOnlyMemory<byte> croppedRegionBytes,
        OcrMathBlock originalBlock,
        CancellationToken ct)
    {
        if (croppedRegionBytes.IsEmpty)
            throw new ArgumentException("Crop must be non-empty.", nameof(croppedRegionBytes));
        if (croppedRegionBytes.Length > _options.MaxImageBytes)
            throw new ArgumentException(
                $"Crop exceeds MaxImageBytes={_options.MaxImageBytes:N0} bytes.", nameof(croppedRegionBytes));

        var sw = Stopwatch.StartNew();

        var payload = new MathpixTextRequest(
            Src: "data:image/png;base64," + Convert.ToBase64String(croppedRegionBytes.Span),
            Formats: new[] { "latex_styled" },
            Ocr: new[] { "math", "text" });

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("text", payload, JsonOpts, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout or a Polly-wrapped timeout — translate to
            // OcrCircuitOpenException so Layer 4 treats it as "pass-through".
            _log?.LogWarning("[OCR_CASCADE] Mathpix timeout — signalling circuit-open to Layer 4");
            throw new OcrCircuitOpenException("Mathpix request timed out.");
        }
        catch (HttpRequestException ex)
        {
            if (IsBrokenCircuitException(ex))
            {
                _log?.LogInformation("[OCR_CASCADE] Mathpix circuit open (RDY-012)");
                throw new OcrCircuitOpenException("Mathpix circuit breaker open.", ex);
            }
            // Genuine transport failure — translate to OcrCircuitOpenException
            // too, so Layer 4 degrades gracefully rather than failing the block.
            _log?.LogWarning(ex, "[OCR_CASCADE] Mathpix HTTP transport failure");
            throw new OcrCircuitOpenException("Mathpix transport failure.", ex);
        }
        catch (Exception ex) when (IsBrokenCircuitException(ex))
        {
            _log?.LogInformation("[OCR_CASCADE] Mathpix circuit open (RDY-012)");
            throw new OcrCircuitOpenException("Mathpix circuit breaker open.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafelyReadStringAsync(response.Content, ct).ConfigureAwait(false);
                _log?.LogWarning(
                    "[OCR_CASCADE] Mathpix returned {Status} — {Body}",
                    (int)response.StatusCode, Truncate(body, 300));
                // Non-2xx isn't a circuit-open; it's a real API error. Fall
                // through to "rescue unsuccessful" rather than propagating.
                return FailureResult(originalBlock, $"mathpix_http_{(int)response.StatusCode}", sw);
            }

            MathpixTextResponse? payloadResult;
            try
            {
                payloadResult = await response.Content
                    .ReadFromJsonAsync<MathpixTextResponse>(JsonOpts, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[OCR_CASCADE] Mathpix response malformed");
                return FailureResult(originalBlock, "mathpix_malformed_response", sw);
            }

            if (payloadResult is null || !string.IsNullOrEmpty(payloadResult.Error))
            {
                return FailureResult(originalBlock,
                    $"mathpix_error:{Truncate(payloadResult?.Error, 80)}", sw);
            }

            var latex = payloadResult.LatexStyled?.Trim();
            if (string.IsNullOrEmpty(latex))
            {
                return FailureResult(originalBlock, "mathpix_no_latex", sw);
            }

            var confidence = Math.Clamp(payloadResult.Confidence ?? 0.9, 0.0, 1.0);
            sw.Stop();
            _log?.LogDebug(
                "[OCR_CASCADE] Mathpix rescue ok latexLen={Len} conf={Conf:F2} latencyMs={Ms}",
                latex.Length, confidence, sw.Elapsed.TotalMilliseconds);

            return originalBlock with
            {
                Latex = latex,
                Confidence = confidence,
                // SympyParsed + CanonicalForm are reset — Layer 5 re-validates
                // the new LaTeX through the CAS router.
                SympyParsed = false,
                CanonicalForm = null,
            };
        }
    }

    private static OcrMathBlock FailureResult(OcrMathBlock original, string reason, Stopwatch sw)
    {
        // Non-rescue outcome — keep original block, but caller records the
        // attempt in fallbacks_fired. We do NOT throw so the cascade can
        // continue with the original (possibly low-confidence) content.
        // Returning the original unchanged is the agreed pass-through contract.
        _ = reason;
        _ = sw;
        return original;
    }

    /// <summary>
    /// Polly's <c>BrokenCircuitException</c> lives in Polly.dll (a dep of the
    /// host's pipeline), not this assembly. We match on type name so we don't
    /// need a hard reference here.
    /// </summary>
    internal static bool IsBrokenCircuitException(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is null) break;
            var name = e.GetType().Name;
            if (name is "BrokenCircuitException" or "IsolatedCircuitException")
                return true;
        }
        return false;
    }

    private static async Task<string> SafelyReadStringAsync(HttpContent c, CancellationToken ct)
    {
        try { return await c.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return string.Empty; }
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Length <= max ? s : s[..max];
    }

    // -------------------------------------------------------------------------
    // Wire DTOs
    // -------------------------------------------------------------------------
    private sealed record MathpixTextRequest(
        [property: JsonPropertyName("src")]     string Src,
        [property: JsonPropertyName("formats")] string[] Formats,
        [property: JsonPropertyName("ocr")]     string[] Ocr);

    private sealed record MathpixTextResponse(
        [property: JsonPropertyName("latex_styled")]    string? LatexStyled,
        [property: JsonPropertyName("confidence")]      double? Confidence,
        [property: JsonPropertyName("confidence_rate")] double? ConfidenceRate,
        [property: JsonPropertyName("error")]           string? Error,
        [property: JsonPropertyName("request_id")]      string? RequestId);
}
