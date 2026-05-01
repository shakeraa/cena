// =============================================================================
// Cena Platform — Anthropic connection probe (real)
//
// Sends a 1-token messages.create call to Anthropic to validate that:
//   - the API key authenticates,
//   - the configured model id is accepted,
//   - the host can reach Anthropic at all.
//
// Categorizes failures so the SPA can show "Invalid key", "Model not found",
// "Rate limited", "Network unreachable" rather than a generic "Failed."
// Never throws — every failure path returns ConnectionTestResult.Fail.
// =============================================================================

using System.Net;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.AiSettings;

public sealed class AnthropicConnectionProbe : IAnthropicConnectionProbe
{
    private readonly ILogger<AnthropicConnectionProbe> _logger;

    public AnthropicConnectionProbe(ILogger<AnthropicConnectionProbe> logger)
    {
        _logger = logger;
    }

    public async Task<ConnectionTestResult> ProbeAsync(
        string apiKey,
        string modelId,
        string? baseUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return ConnectionTestResult.Fail("API key is empty", "CONFIG_MISSING_KEY");
        if (string.IsNullOrWhiteSpace(modelId))
            return ConnectionTestResult.Fail("Model id is empty", "CONFIG_MISSING_MODEL");

        var clientOpts = new ClientOptions { ApiKey = apiKey, MaxRetries = 0 };
        if (!string.IsNullOrWhiteSpace(baseUrl))
            clientOpts.BaseUrl = baseUrl;

        var client = new AnthropicClient(clientOpts);

        // Smallest valid messages.create — 1 user message, 1 token cap. Enough
        // to exercise auth + model resolution without burning budget.
        var probe = new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = 1,
            Messages = new List<MessageParam>
            {
                new MessageParam { Role = "user", Content = "ping" }
            },
        };

        try
        {
            // Anthropic SDK exposes a sync .Create that internally awaits;
            // wrap on a worker thread so the cancellation token is honoured
            // and the request doesn't block the calling synchronization context.
            var response = await Task.Run(() => client.Messages.Create(probe), ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Anthropic probe OK (model={Model}, returnedModel={ReturnedModel})",
                modelId, response.Model);

            return ConnectionTestResult.Ok(
                $"Authenticated. Model '{response.Model}' acknowledged the probe.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Categorize(ex, modelId);
        }
    }

    /// <summary>
    /// Map the Anthropic SDK's exception surface (which wraps HTTP errors as
    /// generic exceptions with status codes embedded in the message) into
    /// stable categories the SPA can render. The SDK's typed error hierarchy
    /// changes between minor versions, so we match on HTTP semantics where
    /// available and fall back to message inspection.
    ///
    /// Order is significant: more-specific 400 sub-cases (credit balance,
    /// model rejection) are checked BEFORE the general 401-style auth match
    /// so a "credit balance too low" 400 doesn't get miscategorized as
    /// AUTH_FAILED.
    /// </summary>
    private ConnectionTestResult Categorize(Exception ex, string modelId)
    {
        var message = ex.Message ?? "";

        // Always log the raw upstream message at debug-friendly level so an
        // operator can inspect why categorization landed where it did. The
        // category-specific log lines below intentionally omit the upstream
        // text (PII / token leak risk on hot paths); this single line is the
        // diagnostic source of truth for "why did the probe say X?".
        _logger.LogInformation(
            "Anthropic probe raw failure (model={Model}, exType={ExType}): {Message}",
            modelId, ex.GetType().Name, message);

        // 400 / insufficient credit balance — Anthropic returns this as
        // invalid_request_error rather than 402, so it MUST be checked
        // before the generic invalid_request_error match below.
        if (message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient_credit", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Anthropic probe rejected: insufficient credit balance");
            return ConnectionTestResult.Fail(
                "Insufficient Anthropic credit balance — top up your account at console.anthropic.com",
                "INSUFFICIENT_CREDITS");
        }

        // 401 / authentication
        if (message.Contains("401", StringComparison.Ordinal)
            || message.Contains("authentication_error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid x-api-key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Anthropic probe rejected: invalid API key");
            return ConnectionTestResult.Fail("Invalid API key", "AUTH_FAILED");
        }

        // 404 / unknown model — Anthropic returns 404 with model_not_found
        if (message.Contains("404", StringComparison.Ordinal)
            || message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not_found_error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Anthropic probe rejected: model '{Model}' not found", modelId);
            return ConnectionTestResult.Fail(
                $"Model '{modelId}' not found or not accessible to this key",
                "MODEL_NOT_FOUND");
        }

        // 429 / rate limited
        if (message.Contains("429", StringComparison.Ordinal)
            || message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Anthropic probe rejected: rate limited");
            return ConnectionTestResult.Fail("Rate limited by Anthropic", "RATE_LIMITED");
        }

        // 5xx / upstream
        if (message.Contains("500", StringComparison.Ordinal)
            || message.Contains("502", StringComparison.Ordinal)
            || message.Contains("503", StringComparison.Ordinal)
            || message.Contains("api_error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("internal_server_error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Anthropic probe failed: upstream error");
            return ConnectionTestResult.Fail("Anthropic upstream error", "UPSTREAM_ERROR");
        }

        // 400 / invalid_request_error — generic catch (model rejection,
        // malformed body, payload too large, etc.). Surfaces the upstream
        // message so the operator sees the actual reason without having to
        // tail container logs.
        if (message.Contains("400", StringComparison.Ordinal)
            || message.Contains("invalid_request_error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Anthropic probe rejected: invalid request");
            return ConnectionTestResult.Fail(
                $"Anthropic rejected the request: {ex.Message}",
                "INVALID_REQUEST");
        }

        // Network / DNS / TLS
        if (ex is HttpRequestException || ex.InnerException is System.Net.Sockets.SocketException)
        {
            _logger.LogWarning(ex, "Anthropic probe failed: network unreachable");
            return ConnectionTestResult.Fail(
                $"Cannot reach Anthropic: {ex.Message}", "NETWORK_UNREACHABLE");
        }

        // Timeout
        if (ex is TaskCanceledException || ex is TimeoutException)
        {
            _logger.LogWarning(ex, "Anthropic probe timed out");
            return ConnectionTestResult.Fail("Probe timed out", "TIMEOUT");
        }

        // Unknown — surface the message so the operator sees something useful
        _logger.LogError(ex, "Anthropic probe failed: unexpected error");
        return ConnectionTestResult.Fail($"Unexpected error: {ex.Message}", "UNEXPECTED_ERROR");
    }
}
