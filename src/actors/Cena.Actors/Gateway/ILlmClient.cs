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
    string? JsonSchema = null);

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
