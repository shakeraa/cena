// =============================================================================
// Cena Platform — Tutor LLM Service Interface (HARDEN TutorEndpoints)
// Production-grade LLM integration with streaming support
// =============================================================================

namespace Cena.Actors.Tutor;

/// <summary>
/// Context for a tutoring session LLM request.
/// </summary>
public sealed record TutorContext(
    string StudentId,
    string ThreadId,
    IReadOnlyList<TutorMessage> MessageHistory,
    string? Subject,
    int? CurrentGrade
);

/// <summary>
/// A message in the tutoring conversation history.
/// </summary>
public sealed record TutorMessage(
    string Role, // 'user' | 'assistant' | 'system'
    string Content
);

/// <summary>
/// A chunk from the LLM stream response.
/// </summary>
public sealed record LlmChunk(
    string Delta,
    bool Finished,
    int? TokensUsed,
    string? Model
);

/// <summary>
/// Service interface for LLM-powered tutoring.
/// Production-grade: real LLM calls with SSE streaming, no stubs.
/// </summary>
public interface ITutorLlmService
{
    /// <summary>
    /// Stream completion tokens from the LLM for tutoring.
    /// </summary>
    /// <param name="context">Tutoring context including history and metadata</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async enumerable of chunks containing tokens and metadata</returns>
    IAsyncEnumerable<LlmChunk> StreamCompletionAsync(
        TutorContext context,
        CancellationToken ct = default);
}
