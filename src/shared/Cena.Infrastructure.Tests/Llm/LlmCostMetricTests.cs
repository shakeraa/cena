// =============================================================================
// Cena Platform — LlmCostMetric unit tests (prr-046)
//
// Guards:
//   - Record emits a single measurement with the expected labels on success
//   - "unknown" is emitted when institute_id is null/empty (not dropped)
//   - FeatureTagAttribute validates lowercase kebab-case
//   - DelegatesLlmCostAttribute validates non-empty inner service
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Llm;
using Xunit;

namespace Cena.Infrastructure.Tests.Llm;

public sealed class LlmCostMetricTests
{
    private sealed class CapturingMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public Meter Create(string name, string? version = null) =>
            new(new MeterOptions(name) { Version = version });
        public void Dispose() { }
    }

    private static LlmPricingTable SamplePricing() =>
        LlmPricingTable.LoadFromYaml("""
            models:
              test_model:
                model_id: "test-sonnet"
                cost_per_input_mtok: 3.00
                cost_per_output_mtok: 15.00
            """);

    [Fact]
    public void Record_EmitsCounter_WithFullLabelSet()
    {
        using var factory = new CapturingMeterFactory();
        var metric = new LlmCostMetric(factory, SamplePricing());

        var capturedValues = new List<double>();
        var capturedTags = new List<Dictionary<string, object?>>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, lst) =>
            {
                if (inst.Name == LlmCostMetric.CostCounterName)
                    lst.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            capturedValues.Add(value);
            var dict = new Dictionary<string, object?>();
            foreach (var t in tags) dict[t.Key] = t.Value;
            capturedTags.Add(dict);
        });
        listener.Start();

        metric.Record(
            feature: "socratic",
            tier: "tier3",
            task: "socratic_question",
            modelId: "test-sonnet",
            inputTokens: 1000,
            outputTokens: 500,
            instituteId: "cena-platform");

        Assert.Single(capturedValues);
        var expectedCost = (1000 * 3.00 + 500 * 15.00) / 1_000_000.0;
        Assert.Equal(expectedCost, capturedValues[0], precision: 9);

        var tags = capturedTags[0];
        Assert.Equal("socratic", tags["feature"]);
        Assert.Equal("tier3", tags["tier"]);
        Assert.Equal("socratic_question", tags["task"]);
        Assert.Equal("cena-platform", tags["institute_id"]);
        Assert.Equal("test-sonnet", tags["model_id"]);
    }

    [Fact]
    public void Record_EmitsUnknownInstitute_WhenNullOrEmpty()
    {
        using var factory = new CapturingMeterFactory();
        var metric = new LlmCostMetric(factory, SamplePricing());

        string? capturedInstitute = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, lst) =>
            {
                if (inst.Name == LlmCostMetric.CostCounterName)
                    lst.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == "institute_id") capturedInstitute = (string?)t.Value;
            }
        });
        listener.Start();

        metric.Record("socratic", "tier3", "socratic_question", "test-sonnet", 1, 1, null);
        Assert.Equal(LlmCostMetric.UnknownInstituteLabel, capturedInstitute);

        metric.Record("socratic", "tier3", "socratic_question", "test-sonnet", 1, 1, "   ");
        Assert.Equal(LlmCostMetric.UnknownInstituteLabel, capturedInstitute);
    }

    [Fact]
    public void Record_ThrowsOnEmptyFeature()
    {
        using var factory = new CapturingMeterFactory();
        var metric = new LlmCostMetric(factory, SamplePricing());

        Assert.Throws<ArgumentException>(() =>
            metric.Record("", "tier3", "socratic_question", "test-sonnet", 1, 1));
    }

    [Fact]
    public void Record_ThrowsOnUnknownModel_ViaPricingTable()
    {
        using var factory = new CapturingMeterFactory();
        var metric = new LlmCostMetric(factory, SamplePricing());

        // Fail-loud: unknown model → no silent $0 emission.
        Assert.Throws<InvalidOperationException>(() =>
            metric.Record("socratic", "tier3", "socratic_question", "gpt-4", 1, 1));
    }

    [Fact]
    public void FeatureTagAttribute_AcceptsValidKebabCase()
    {
        var attr = new FeatureTagAttribute("hint-l2");
        Assert.Equal("hint-l2", attr.Feature);
    }

    [Theory]
    [InlineData("Socratic")]       // uppercase
    [InlineData("hint_l2")]        // underscore
    [InlineData("hint l2")]         // space
    [InlineData("HintL2")]          // camelCase
    [InlineData("hint.l2")]         // dot
    public void FeatureTagAttribute_RejectsInvalidFeatureNames(string bad)
    {
        Assert.Throws<ArgumentException>(() => new FeatureTagAttribute(bad));
    }

    [Fact]
    public void FeatureTagAttribute_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new FeatureTagAttribute(""));
        Assert.Throws<ArgumentException>(() => new FeatureTagAttribute("   "));
    }

    [Fact]
    public void DelegatesLlmCostAttribute_RequiresNonEmptyInnerService()
    {
        Assert.Throws<ArgumentException>(() => new DelegatesLlmCostAttribute(""));
        var attr = new DelegatesLlmCostAttribute("ClaudeTutorLlmService");
        Assert.Equal("ClaudeTutorLlmService", attr.InnerService);
    }

    [Fact]
    public void NullLlmCostMetric_IsNoOp()
    {
        // Smoke test: the no-op must not throw for any input.
        NullLlmCostMetric.Instance.Record("f", "tier2", "t", "m", 1, 1, null);
        NullLlmCostMetric.Instance.Record("g", "tier3", "t", "m", 0, 0);
    }
}
