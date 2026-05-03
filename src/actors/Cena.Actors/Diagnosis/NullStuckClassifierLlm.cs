// =============================================================================
// Cena Platform — NullStuckClassifierLlm (RDY-063 Phase 1)
//
// No-op implementation wired in when no Anthropic API key is configured
// or when running in tests that must not hit the network. Returns
// a clean "llm_disabled" error so HybridStuckClassifier treats the
// case as heuristic-only.
// =============================================================================

namespace Cena.Actors.Diagnosis;

public sealed class NullStuckClassifierLlm : IStuckClassifierLlm
{
    public Task<LlmClassificationResult> ClassifyAsync(
        StuckContext ctx, CancellationToken ct = default)
    {
        return Task.FromResult(LlmClassificationResult.Invalid("llm_disabled", 0));
    }
}
