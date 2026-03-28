// =============================================================================
// Cena Platform — Anthropic LLM Client (Claude Sonnet/Haiku)
// SAI-00: Real API calls via official Anthropic SDK v12.x
// No retries, no streaming — circuit breaker handles resilience.
// =============================================================================

using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Gateway;

public sealed class AnthropicLlmClient : ILlmClient, IDisposable
{
    private readonly AnthropicClient _client;
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

        _defaultModelId = configuration["LLM:Anthropic:ModelId"] ?? "claude-sonnet-4-6";
        _client = new AnthropicClient { ApiKey = apiKey };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var modelId = request.ModelId ?? _defaultModelId;
        var temperature = request.JsonSchema is not null ? 0.0 : (double)request.Temperature;

        var sw = Stopwatch.StartNew();

        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = modelId,
                MaxTokens = request.MaxTokens,
                System = request.SystemPrompt,
                Temperature = temperature,
                Messages = [new() { Role = Role.User, Content = request.UserPrompt }],
            }, ct);

            sw.Stop();

            var text = string.Join("", response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(b => b.Text));

            var inputTokens = response.Usage?.InputTokens ?? 0;
            var outputTokens = response.Usage?.OutputTokens ?? 0;

            _logger.LogInformation(
                "LLM call: Model={Model} InputTokens={Input} OutputTokens={Output} Latency={Latency:F0}ms",
                modelId, inputTokens, outputTokens, sw.Elapsed.TotalMilliseconds);

            return new LlmResponse(text, (int)inputTokens, (int)outputTokens, sw.Elapsed, modelId, FromCache: false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "LLM call failed: Model={Model} Latency={Latency:F0}ms", modelId, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public void Dispose() => _client.Dispose();
}
