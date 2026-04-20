// =============================================================================
// Cena Platform — DI registration for LLM cost metric (prr-046)
//
// Hosts call AddLlmCostMetric(yamlPath) during startup to register:
//   - LlmPricingTable (singleton, loaded once from routing-config.yaml)
//   - ILlmCostMetric → LlmCostMetric (singleton)
//
// WHY a dedicated extension:
//   The pricing table has a filesystem dependency (routing-config.yaml) and
//   is loaded fail-loud at startup. Hosts already know where their
//   contracts/ directory is (Actors.Host, Admin.Api.Host, Student.Api.Host
//   all live under src/ and resolve repo paths via Directory.GetCurrentDirectory
//   or ContentRootPath). Centralising the load keeps each host's Program.cs
//   honest: one line in, a single crash at startup if the YAML is missing or
//   malformed — no silent $0 cost counter at runtime.
//
// Hosts pass the absolute path to routing-config.yaml (usually resolved
// relative to ContentRoot). Tests can pass a temporary YAML path.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Infrastructure.Llm;

/// <summary>
/// DI extensions for registering the prr-046 cost metric stack.
/// </summary>
public static class LlmCostMetricRegistration
{
    /// <summary>
    /// Default repo-relative path to the routing-config.yaml, used when a
    /// caller doesn't supply one. Hosts typically resolve
    /// <c>Path.Combine(contentRoot, DefaultRoutingConfigRelativePath)</c>.
    /// </summary>
    public const string DefaultRoutingConfigRelativePath = "contracts/llm/routing-config.yaml";

    /// <summary>
    /// Register the pricing table and cost metric services.
    /// Pricing is loaded fail-loud (throws at startup if the file is missing
    /// or a model row lacks cost_per_input_mtok / cost_per_output_mtok) —
    /// intentional per task DoD non-negotiable.
    /// </summary>
    public static IServiceCollection AddLlmCostMetric(
        this IServiceCollection services,
        string routingConfigYamlPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingConfigYamlPath);

        // Singleton — the YAML is immutable after process start and the
        // Dictionary<string, Rate> is safe to share across threads.
        services.TryAddSingleton(_ => LlmPricingTable.LoadFromFile(routingConfigYamlPath));
        services.TryAddSingleton<ILlmCostMetric, LlmCostMetric>();
        return services;
    }

    /// <summary>
    /// Overload that accepts a caller-provided pricing table. Used by tests
    /// that construct an in-memory <see cref="LlmPricingTable"/> so they
    /// don't need a real YAML on disk.
    /// </summary>
    public static IServiceCollection AddLlmCostMetric(
        this IServiceCollection services,
        LlmPricingTable pricingTable)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pricingTable);

        services.TryAddSingleton(pricingTable);
        services.TryAddSingleton<ILlmCostMetric, LlmCostMetric>();
        return services;
    }
}
