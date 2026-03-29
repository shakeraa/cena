namespace Cena.LlmAcl;

public interface ILlmGateway
{
    /// <summary>
    /// Send a completion request through the ACL layer.
    /// Handles routing, circuit breaking, rate limiting, and usage tracking.
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if the global rate limit allows another request.
    /// </summary>
    bool CanMakeRequest(string modelTier);
}

public record LlmRequest(
    string ModelPreference,
    string SystemPrompt,
    string UserMessage,
    float Temperature = 0.3f,
    int MaxTokens = 2048,
    string? CallerContext = null
);

public record LlmResponse(
    string Content,
    string ModelUsed,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency
);
