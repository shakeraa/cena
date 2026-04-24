// =============================================================================
// Cena Platform — Tutor LLM Service Interface (HARDEN TutorEndpoints)
// Production-grade LLM integration with streaming support
// =============================================================================

namespace Cena.Actors.Tutor;

/// <summary>
/// Context for a tutoring session LLM request.
/// </summary>
/// <param name="StudentId">Globally-unique student identifier.</param>
/// <param name="ThreadId">Tutor conversation thread identifier.</param>
/// <param name="MessageHistory">Prior turns in this thread, oldest first.</param>
/// <param name="Subject">Subject area (e.g. Algebra). Optional.</param>
/// <param name="CurrentGrade">Student grade level. Optional.</param>
/// <param name="InstituteId">
/// Optional tenant identifier (ADR-0001). Threaded through to finops gates
/// (DailyTutorTimeBudget) so per-institute cap overrides and per-institute
/// metric labels apply. Null when the caller has not resolved the tenant;
/// metrics will tag as "unknown" and the platform default cap applies.
/// </param>
public sealed record TutorContext(
    string StudentId,
    string ThreadId,
    IReadOnlyList<TutorMessage> MessageHistory,
    string? Subject,
    int? CurrentGrade,
    string? InstituteId = null
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
