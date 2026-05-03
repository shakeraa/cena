// =============================================================================
// Tests for AnthropicSupportedModels — closed-set + per-Mtok pricing pinning.
//
// The cost-meter pinning test (CostMeterShifts_OnHaikuToSonnetOverride) is
// load-bearing per the task DoD: an override that flips Haiku → Sonnet
// MUST raise the cost-per-call from $1/$5 to $3/$15 per Mtok. If anyone
// edits AnthropicSupportedModels without updating this test, the test
// fails — which is intentional.
// =============================================================================

using Cena.Admin.Api.AiSettings;

namespace Cena.Admin.Api.Tests.AiSettings;

public sealed class AnthropicSupportedModelsTests
{
    [Fact]
    public void AllList_IsNonEmpty_AndHasUniqueModelIds()
    {
        Assert.NotEmpty(AnthropicSupportedModels.All);

        var ids = AnthropicSupportedModels.All.Select(m => m.ModelId).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("claude-opus-4-7")]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("claude-haiku-4-5-20260101")]
    public void IsSupported_ReturnsTrue_ForCanonicalSet(string modelId)
    {
        Assert.True(AnthropicSupportedModels.IsSupported(modelId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("gpt-4")]
    [InlineData("kimi-k2-turbo")]
    [InlineData("claude-bogus-1-0")]
    public void IsSupported_ReturnsFalse_ForOutsideSet(string? modelId)
    {
        Assert.False(AnthropicSupportedModels.IsSupported(modelId));
    }

    [Fact]
    public void TryGet_RoundtripsPricingMetadata()
    {
        var sonnet = AnthropicSupportedModels.TryGet("claude-sonnet-4-6");
        Assert.NotNull(sonnet);
        Assert.Equal(3.00m, sonnet!.InputUsdPerMtok);
        Assert.Equal(15.00m, sonnet.OutputUsdPerMtok);
        Assert.Equal("sonnet", sonnet.Tier);
    }

    /// <summary>
    /// Cost-meter pinning test (per task DoD). When the curator flips
    /// concept_extraction Haiku → Sonnet, the cost per call MUST raise
    /// from $1/$5 to $3/$15 per Mtok. This test guards against silent
    /// regression of the pricing table — any edit to
    /// AnthropicSupportedModels.All that breaks the cost mapping fails
    /// here and the cost dashboard stays accurate.
    /// </summary>
    [Fact]
    public void CostMeterShifts_OnHaikuToSonnetOverride()
    {
        var haikuPricing = AnthropicSupportedModels.ResolvePricingFor("claude-haiku-4-5-20251001");
        Assert.Equal(1.00m, haikuPricing.InputUsdPerMtok);
        Assert.Equal(5.00m, haikuPricing.OutputUsdPerMtok);

        // Curator flips override to Sonnet — every call site that resolves
        // through ModelResolver now sees this model id, and EmitMetrics
        // / per-feature cost must charge Sonnet rates not Haiku rates.
        var sonnetPricing = AnthropicSupportedModels.ResolvePricingFor("claude-sonnet-4-6");
        Assert.Equal(3.00m, sonnetPricing.InputUsdPerMtok);
        Assert.Equal(15.00m, sonnetPricing.OutputUsdPerMtok);

        // Numerical pinning: a 10k-input / 1k-output call costs more on
        // Sonnet than Haiku — exact ratio (3x input, 3x output) makes
        // this an audit-grade assertion not a sanity check.
        var haikuCallCost = (10_000m * haikuPricing.InputUsdPerMtok
                             + 1_000m * haikuPricing.OutputUsdPerMtok) / 1_000_000m;
        var sonnetCallCost = (10_000m * sonnetPricing.InputUsdPerMtok
                              + 1_000m * sonnetPricing.OutputUsdPerMtok) / 1_000_000m;
        Assert.True(sonnetCallCost > haikuCallCost,
            $"Sonnet ($ {sonnetCallCost}) must cost more than Haiku ($ {haikuCallCost})");
        Assert.Equal(3.00m, sonnetCallCost / haikuCallCost);
    }

    [Fact]
    public void ResolvePricingFor_UnknownModelId_FallsBackToSonnet()
    {
        // Defensive fallback — unknown model id returns Sonnet rates so
        // the cost dashboard is conservatively over-priced rather than
        // under-priced. Caller emits a warning when this branch fires.
        var pricing = AnthropicSupportedModels.ResolvePricingFor("model-that-does-not-exist");
        Assert.Equal(LlmCallPricing.AnthropicSonnet4_6, pricing);
    }

    [Fact]
    public void All_RowsMatch_RoutingConfigYamlSection1Models()
    {
        // Sanity contract: AnthropicSupportedModels and routing-config.yaml
        // models: section MUST be in sync. Walk up the worktree to load
        // the file and assert every supported model id has a matching
        // pricing row whose rates equal AnthropicSupportedModels'.
        // Skipped if the file isn't found (tests can run outside the
        // repo tree, e.g. in published-binary CI).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", "llm", "routing-config.yaml");
            if (File.Exists(candidate)) { path = candidate; break; }
            dir = dir.Parent;
        }
        if (path is null) return; // not a hard failure — just skip in headless CI

        var pricingTable = Cena.Infrastructure.Llm.LlmPricingTable.LoadFromFile(path);
        foreach (var supported in AnthropicSupportedModels.All)
        {
            var rate = pricingTable.Resolve(supported.ModelId);
            Assert.Equal((double)supported.InputUsdPerMtok, rate.InputPerMTok);
            Assert.Equal((double)supported.OutputUsdPerMtok, rate.OutputPerMTok);
        }
    }
}
