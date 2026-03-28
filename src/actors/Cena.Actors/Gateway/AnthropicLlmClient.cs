// =============================================================================
// Cena Platform — Anthropic LLM Client (Claude Sonnet/Haiku)
// SAI-00: Real API calls via official Anthropic SDK
// No retries, no streaming — circuit breaker handles resilience.
// =============================================================================

using System.Diagnostics;
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Gateway;

public sealed class AnthropicLlmClient : ILlmClient, IDisposable
{
    private readonly AnthropicApi _api;
    private readonly string _defaultModelId;
    private readonly ILogger<AnthropicLlmClient> _logger;

    public AnthropicLlmClient(IConfiguration configuration, ILogger<AnthropicLlmClient> logger)
    {
        _logger = logger;

        var apiKey = configuration["LLM:Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("LLM:Anthropic:ApiKey not configured — LLM calls will fail at runtime");
            apiKey = "not-configured";
        }

        _defaultModelId = configuration["LLM:Anthropic:ModelId"] ?? "claude-sonnet-4-6-20260215";
        _api = new AnthropicApi();
        _api.AuthorizeUsingApiKey(apiKey);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var modelId = request.ModelId ?? _defaultModelId;
        var temperature = request.JsonSchema is not null ? 0.0 : (double)request.Temperature;

        var sw = Stopwatch.StartNew();

        try
        {
            var response = await _api.CreateMessageAsync(
                model: modelId,
                messages: [request.UserPrompt],
                system: request.SystemPrompt,
                maxTokens: request.MaxTokens,
                temperature: temperature,
                cancellationToken: ct);

            sw.Stop();

            // Extract text from OneOf<string, IList<Block>> response
            string text;
            if (response.Content.IsValue1)
            {
                text = response.Content.Value1 ?? "";
            }
            else
            {
                text = string.Join("", response.Content.Value2!
                    .Where(b => b.IsText)
                    .Select(b => b.Text!.Text));
            }
            var inputTokens = response.Usage?.InputTokens ?? 0;
            var outputTokens = response.Usage?.OutputTokens ?? 0;

            _logger.LogInformation(
                "LLM call: Model={Model} InputTokens={Input} OutputTokens={Output} Latency={Latency:F0}ms",
                modelId, inputTokens, outputTokens, sw.Elapsed.TotalMilliseconds);

            return new LlmResponse(text, inputTokens, outputTokens, sw.Elapsed, modelId, FromCache: false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "LLM call failed: Model={Model} Latency={Latency:F0}ms", modelId, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public void Dispose() => _api.Dispose();
}
