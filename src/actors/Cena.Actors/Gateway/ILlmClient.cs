// =============================================================================
// Cena Platform -- ILlmClient Abstraction
// Layer: LLM ACL | Runtime: .NET 9
//
// Provider-agnostic interface for LLM completion requests.
// Implementations: AnthropicLlmClient, (future) MoonshotLlmClient.
// Retries/fallback handled by LlmCircuitBreakerActor — not here.
// =============================================================================

namespace Cena.Actors.Gateway;

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    float Temperature = 0.7f,
    int MaxTokens = 4096,
    string? ModelId = null,
    string? JsonSchema = null,
    // SAI-004: Prompt caching -- when true, system prompt is sent with
    // cache_control: { type: "ephemeral" } per routing-config.yaml section 6.
    bool CacheSystemPrompt = false);

public sealed record LlmResponse(
    string Content,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    string ModelId,
    bool FromCache)
{
    /// <summary>Alias for Content — used by callers that expect a Text property.</summary>
    public string Text => Content;
}
