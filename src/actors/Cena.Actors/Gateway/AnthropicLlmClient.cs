// =============================================================================
// Cena Platform — Anthropic LLM Client (Claude Sonnet/Haiku)
// SAI-00: Real API calls via official Anthropic SDK v12.x
// No retries, no streaming — circuit breaker handles resilience.
//
// prr-143: This is the central trace-id stamping site for the ILlmClient
// seam. Every service that calls ILlmClient.CompleteAsync gets a stamped
// trace_id on its outbound call attempt + success/failure log line. Services
// that do NOT go through ILlmClient (they invoke Anthropic SDK directly —
// ClaudeTutorLlmService, ClaudeStuckClassifierLlm, admin.api AI generators)
// stamp trace_id themselves; the EveryLlmServiceEmitsTraceIdTest arch test
// ensures either the service or this gateway stamps on every [TaskRouting]
// path.
// =============================================================================

using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Gateway;

public sealed class AnthropicLlmClient : ILlmClient, IDisposable
{
    private readonly AnthropicClient _client;
    private readonly string _defaultModelId;
    private readonly ILogger<AnthropicLlmClient> _logger;
    private readonly IActivityPropagator? _activityPropagator;

    public AnthropicLlmClient(
        IConfiguration configuration,
        ILogger<AnthropicLlmClient> logger,
        IActivityPropagator? activityPropagator = null)
    {
        _logger = logger;
        _activityPropagator = activityPropagator;

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

        // prr-143: trace-id on every LLM call attempt. Resolved once per call
        // so success and failure paths stitch to the same trace in the
        // observability backend. When no IActivityPropagator is registered
        // (tests, legacy hosts), trace_id stamping is a no-op.
        var traceId = _activityPropagator?.GetTraceId();
        using var activity = _activityPropagator?.StartLlmActivity("llm_gateway");
        activity?.SetTag("trace_id", traceId);
        activity?.SetTag("model_id", modelId);

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

            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", inputTokens);
            activity?.SetTag("output_tokens", outputTokens);
            _logger.LogInformation(
                "LLM call (trace_id={TraceId}): Model={Model} InputTokens={Input} OutputTokens={Output} Latency={Latency:F0}ms",
                traceId, modelId, inputTokens, outputTokens, sw.Elapsed.TotalMilliseconds);

            return new LlmResponse(text, (int)inputTokens, (int)outputTokens, sw.Elapsed, modelId, FromCache: false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetTag("outcome", "error");
            activity?.SetTag("error.type", ex.GetType().Name);
            _logger.LogError(ex,
                "LLM call failed (trace_id={TraceId}): Model={Model} Latency={Latency:F0}ms",
                traceId, modelId, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public void Dispose() => _client.Dispose();
}
