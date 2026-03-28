// =============================================================================
// Cena Platform -- LlmClientRouter
// Layer: LLM ACL | Runtime: .NET 9
//
// Routes LLM requests to the correct provider based on model ID prefix.
// "claude-" -> Anthropic, "kimi-" -> Moonshot (not yet implemented).
// =============================================================================

namespace Cena.Actors.Gateway;

public sealed class LlmClientRouter : ILlmClient
{
    private readonly AnthropicLlmClient _anthropic;

    public LlmClientRouter(AnthropicLlmClient anthropic)
    {
        _anthropic = anthropic ?? throw new ArgumentNullException(nameof(anthropic));
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ModelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
        {
            return _anthropic.CompleteAsync(request, ct);
        }

        if (request.ModelId.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotImplementedException(
                $"Moonshot provider not yet implemented. ModelId='{request.ModelId}'");
        }

        throw new ArgumentException(
            $"Unknown LLM provider for model '{request.ModelId}'. " +
            "Expected prefix: 'claude-' or 'kimi-'.",
            nameof(request));
    }
}
