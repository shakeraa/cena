// =============================================================================
// Cena Platform — NullLlmCostMetric (prr-046)
//
// No-op ILlmCostMetric implementation for unit tests that construct LLM
// services without a meter factory or pricing table. Production hosts MUST
// register LlmCostMetric via AddLlmCostMetric(); this null is strictly for
// tests that don't assert on cost emission.
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// No-op cost metric for tests. Production hosts must use
/// <see cref="LlmCostMetric"/> (registered via
/// <see cref="LlmCostMetricRegistration.AddLlmCostMetric(Microsoft.Extensions.DependencyInjection.IServiceCollection, string)"/>).
/// </summary>
public sealed class NullLlmCostMetric : ILlmCostMetric
{
    /// <summary>Shared singleton — the type is stateless.</summary>
    public static readonly NullLlmCostMetric Instance = new();

    public void Record(
        string feature,
        string tier,
        string task,
        string modelId,
        long inputTokens,
        long outputTokens,
        string? instituteId = null)
    {
        // intentionally no-op
    }
}
