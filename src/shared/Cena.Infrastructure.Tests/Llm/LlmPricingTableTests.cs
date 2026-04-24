// =============================================================================
// Cena Platform — LlmPricingTable unit tests (prr-046)
//
// Guards:
//   - YAML loader extracts model_id + input/output costs from routing-config.yaml
//   - ComputeCostUsd applies the per-MTok math correctly
//   - Resolve fails loud on unknown models (no silent $0 cost)
//   - feature_monthly_ceiling_usd section loads when present
// =============================================================================

using Cena.Infrastructure.Llm;
using Xunit;

namespace Cena.Infrastructure.Tests.Llm;

public sealed class LlmPricingTableTests
{
    private const string SampleYaml = """
        models:
          claude_sonnet_4_6:
            model_id: "claude-sonnet-4-6-20260215"
            provider: "anthropic"
            cost_per_input_mtok: 3.00
            cost_per_output_mtok: 15.00
          claude_haiku_4_5:
            model_id: "claude-haiku-4-5-20260101"
            provider: "anthropic"
            cost_per_input_mtok: 1.00
            cost_per_output_mtok: 5.00
          kimi_k2_5:
            model_id: "kimi-k2.5"
            provider: "moonshot"
            cost_per_input_mtok: 0.45
            cost_per_output_mtok: 2.20

        feature_monthly_ceiling_usd:
          socratic: 12000.00
          classification: 300.00
        """;

    [Fact]
    public void LoadFromYaml_ExtractsAllModelPricing()
    {
        var table = LlmPricingTable.LoadFromYaml(SampleYaml);

        Assert.Equal(3, table.KnownModelIds.Count);
        Assert.Contains("claude-sonnet-4-6-20260215", table.KnownModelIds);
        Assert.Contains("claude-haiku-4-5-20260101", table.KnownModelIds);
        Assert.Contains("kimi-k2.5", table.KnownModelIds);
    }

    [Fact]
    public void Resolve_ReturnsCorrectRates_ForKnownModel()
    {
        var table = LlmPricingTable.LoadFromYaml(SampleYaml);
        var rate = table.Resolve("claude-sonnet-4-6-20260215");
        Assert.Equal(3.00, rate.InputPerMTok);
        Assert.Equal(15.00, rate.OutputPerMTok);
    }

    [Fact]
    public void Resolve_ThrowsWithSpecificMessage_ForUnknownModel()
    {
        var table = LlmPricingTable.LoadFromYaml(SampleYaml);
        var ex = Assert.Throws<InvalidOperationException>(
            () => table.Resolve("gpt-4-does-not-exist"));
        Assert.Contains("gpt-4-does-not-exist", ex.Message);
        Assert.Contains("routing-config.yaml", ex.Message);
    }

    [Theory]
    [InlineData("claude-sonnet-4-6-20260215", 1000L, 500L, 0.003 + 0.0075)]  // 3.0*1000/1e6 + 15.0*500/1e6
    [InlineData("claude-haiku-4-5-20260101", 1000L, 500L, 0.001 + 0.0025)]    // 1.0*1000/1e6 + 5.0*500/1e6
    [InlineData("kimi-k2.5",                 1000L, 500L, 0.00045 + 0.0011)]  // 0.45*1000/1e6 + 2.20*500/1e6
    public void ComputeCostUsd_AppliesPerMTokMath(
        string modelId, long inputTokens, long outputTokens, double expectedUsd)
    {
        var table = LlmPricingTable.LoadFromYaml(SampleYaml);
        var cost = table.ComputeCostUsd(modelId, inputTokens, outputTokens);
        Assert.Equal(expectedUsd, cost, precision: 9);
    }

    [Fact]
    public void ComputeCostUsd_TreatsNegativeTokensAsZero()
    {
        // Anthropic SDK occasionally omits Usage on streaming failures and
        // a downstream map can produce negative deltas; we must not feed
        // those into the counter as negative cost.
        var table = LlmPricingTable.LoadFromYaml(SampleYaml);
        var cost = table.ComputeCostUsd("claude-haiku-4-5-20260101", -100L, -50L);
        Assert.Equal(0, cost);
    }

    [Fact]
    public void LoadFromYaml_LoadsFeatureCeilings()
    {
        var table = LlmPricingTable.LoadFromYaml(SampleYaml);
        Assert.Equal(2, table.FeatureMonthlyCeilingUsd.Count);
        Assert.Equal(12000.00, table.FeatureMonthlyCeilingUsd["socratic"]);
        Assert.Equal(300.00, table.FeatureMonthlyCeilingUsd["classification"]);
    }

    [Fact]
    public void LoadFromYaml_EmptyFeatureCeilings_ReturnsEmptyDictionary()
    {
        const string yamlWithoutCeilings = """
            models:
              x:
                model_id: "m"
                cost_per_input_mtok: 1.0
                cost_per_output_mtok: 2.0
            """;
        var table = LlmPricingTable.LoadFromYaml(yamlWithoutCeilings);
        Assert.Empty(table.FeatureMonthlyCeilingUsd);
    }

    [Fact]
    public void LoadFromYaml_FailsLoudOnMissingModelsSection()
    {
        // If a dev accidentally commits a routing-config.yaml that's lost
        // the models: block, every downstream cost computation would
        // throw — fail at load time, not at first LLM call.
        const string yamlWithoutModels = """
            something_else: true
            """;
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmPricingTable.LoadFromYaml(yamlWithoutModels));
        Assert.Contains("models:", ex.Message);
    }

    [Fact]
    public void LoadFromYaml_FailsLoudOnModelMissingCost()
    {
        const string incompleteYaml = """
            models:
              broken:
                model_id: "broken-model"
                cost_per_input_mtok: 1.0
                # cost_per_output_mtok missing
            """;
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmPricingTable.LoadFromYaml(incompleteYaml));
        Assert.Contains("cost_per_output_mtok", ex.Message);
    }

    [Fact]
    public void LoadFromFile_ActualRoutingConfig_ParsesCleanly()
    {
        // Smoke test against the real file on disk. Fails if the YAML
        // shape drifts; keeps the parser honest to the contract.
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "contracts", "llm", "routing-config.yaml");
        Assert.True(File.Exists(path), $"routing-config.yaml not found at {path}");

        var table = LlmPricingTable.LoadFromFile(path);

        // All model_ids referenced by Cena LLM services must resolve.
        Assert.Contains("claude-sonnet-4-6-20260215", table.KnownModelIds);
        Assert.Contains("claude-haiku-4-5-20260101", table.KnownModelIds);

        // Feature ceilings for the 12 declared cost-centers must be present.
        Assert.Contains("socratic", table.FeatureMonthlyCeilingUsd.Keys);
        Assert.Contains("classification", table.FeatureMonthlyCeilingUsd.Keys);
        Assert.Contains("question-generation", table.FeatureMonthlyCeilingUsd.Keys);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
