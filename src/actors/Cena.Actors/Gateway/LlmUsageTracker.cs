// =============================================================================
// Cena Platform -- LlmUsageTracker
// Layer: LLM ACL | Runtime: .NET 9 | Store: Marten (PostgreSQL)
//
// Records per-request LLM token usage for billing, cost caps, and analytics.
// Document is stored via Marten lightweight session.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Gateway;

/// <summary>
/// Marten document representing a single LLM API call for billing purposes.
/// </summary>
public sealed class LlmUsageDocument
{
    public string Id { get; init; } = $"llm-{Guid.NewGuid():N}";
    public string ModelId { get; init; } = "";
    public string TaskType { get; init; } = "";
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public double CostUsd { get; init; }
    public double LatencyMs { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Persists LLM usage documents to Marten for billing and analytics.
/// </summary>
public sealed class LlmUsageTracker
{
    private readonly IDocumentStore _store;
    private readonly ILogger<LlmUsageTracker> _logger;

    public LlmUsageTracker(IDocumentStore store, ILogger<LlmUsageTracker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records an LLM usage event from a completed response.
    /// </summary>
    public async Task TrackAsync(
        LlmResponse response,
        string taskType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var costUsd = EstimateCost(response.ModelId, response.InputTokens, response.OutputTokens);

        var doc = new LlmUsageDocument
        {
            ModelId = response.ModelId,
            TaskType = taskType,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
            CostUsd = costUsd,
            LatencyMs = response.Latency.TotalMilliseconds,
            Timestamp = DateTimeOffset.UtcNow
        };

        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "LLM usage tracked: Model={Model}, Task={TaskType}, " +
            "InputTokens={InputTokens}, OutputTokens={OutputTokens}, CostUsd={CostUsd:F6}",
            doc.ModelId, doc.TaskType, doc.InputTokens, doc.OutputTokens, doc.CostUsd);
    }

    /// <summary>
    /// Estimates cost in USD based on model pricing from routing-config.yaml.
    /// Prices per million tokens (MTok) — converted to per-token here.
    /// </summary>
    internal static double EstimateCost(string modelId, int inputTokens, int outputTokens)
    {
        var (inputPerMTok, outputPerMTok) = modelId switch
        {
            _ when modelId.StartsWith("claude-opus", StringComparison.OrdinalIgnoreCase)
                => (5.00, 25.00),
            _ when modelId.StartsWith("claude-sonnet", StringComparison.OrdinalIgnoreCase)
                => (3.00, 15.00),
            _ when modelId.StartsWith("claude-haiku", StringComparison.OrdinalIgnoreCase)
                => (1.00, 5.00),
            _ when modelId.StartsWith("kimi-k2.5", StringComparison.OrdinalIgnoreCase)
                => (0.45, 2.20),
            _ when modelId.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase)
                => (0.40, 2.00),
            _ => (3.00, 15.00) // Default to Sonnet pricing
        };

        return (inputTokens * inputPerMTok / 1_000_000.0)
             + (outputTokens * outputPerMTok / 1_000_000.0);
    }
}
