// =============================================================================
// Cena Platform — Null Tutor LLM Service (HARDEN TutorEndpoints)
// Operational failure mode when LLM is not configured
// NOT A STUB — this is a legitimate operational fallback
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Tutor;

/// <summary>
/// Null implementation of ITutorLlmService for when LLM is not configured.
/// This is NOT a stub — it's an operational failure mode that returns
/// a clear error message to the user explaining the service is unavailable.
/// </summary>
public sealed class NullTutorLlmService : ITutorLlmService
{
    private readonly ILogger<NullTutorLlmService> _logger;

    public NullTutorLlmService(ILogger<NullTutorLlmService> logger)
    {
        _logger = logger;
        _logger.LogWarning("NullTutorLlmService activated — LLM is not configured. " +
            "Set CENA_LLM_API_KEY environment variable to enable tutoring.");
    }

    public async IAsyncEnumerable<LlmChunk> StreamCompletionAsync(
        TutorContext context,
        CancellationToken ct = default)
    {
        _logger.LogWarning("LLM request for student {StudentId} rejected — LLM not configured", 
            context.StudentId);

        // Return a single chunk with the error message and finished flag
        yield return new LlmChunk(
            Delta: "The AI tutoring service is not configured. Please contact your administrator to enable this feature.",
            Finished: true,
            TokensUsed: 0,
            Model: "unconfigured"
        );
    }
}
