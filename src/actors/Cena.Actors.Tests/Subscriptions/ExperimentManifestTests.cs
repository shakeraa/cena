// =============================================================================
// Cena Platform — ExperimentManifest + bucketer tests (EPIC-PRR-I PRR-332)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class ExperimentManifestTests
{
    [Fact]
    public void Disabled_experiment_always_returns_first_variant()
    {
        var exp = new ExperimentDefinition(
            "killed", Enabled: false,
            new[]
            {
                new ExperimentVariant("control", 1),
                new ExperimentVariant("treatment", 1),
            });
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal("control", exp.Bucket($"subject-{i}").VariantId);
        }
    }

    [Fact]
    public void Enabled_experiment_is_deterministic_for_same_subject()
    {
        var exp = new ExperimentDefinition(
            "deterministic", Enabled: true,
            new[]
            {
                new ExperimentVariant("A", 1),
                new ExperimentVariant("B", 1),
            });
        var first = exp.Bucket("subject-42").VariantId;
        var second = exp.Bucket("subject-42").VariantId;
        var third = exp.Bucket("subject-42").VariantId;
        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Even_weight_split_is_roughly_balanced_across_many_subjects()
    {
        var exp = new ExperimentDefinition(
            "split", Enabled: true,
            new[]
            {
                new ExperimentVariant("A", 1),
                new ExperimentVariant("B", 1),
            });
        var a = 0;
        var b = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var v = exp.Bucket($"subject-{i}").VariantId;
            if (v == "A") a++; else b++;
        }
        // Each bucket should be in [40%, 60%] with 10k subjects (uniform).
        Assert.InRange(a, 4_000, 6_000);
        Assert.InRange(b, 4_000, 6_000);
    }

    [Fact]
    public void Weighted_split_respects_weights()
    {
        var exp = new ExperimentDefinition(
            "weighted", Enabled: true,
            new[]
            {
                new ExperimentVariant("A", 3),
                new ExperimentVariant("B", 1),
            });
        var a = 0;
        var b = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var v = exp.Bucket($"subject-{i}").VariantId;
            if (v == "A") a++; else b++;
        }
        // A is 3x more likely → ~75%/25%. Generous tolerance ±5%.
        Assert.InRange(a, 7_000, 8_000);
        Assert.InRange(b, 2_000, 3_000);
    }

    [Fact]
    public void Manifest_unknown_experiment_returns_control_sentinel()
    {
        var manifest = new ExperimentManifest(Array.Empty<ExperimentDefinition>());
        var v = manifest.Bucket("does-not-exist", "any");
        Assert.Equal("control", v.VariantId);
    }

    [Fact]
    public void Manifest_known_experiment_delegates_to_definition()
    {
        var manifest = new ExperimentManifest(new[]
        {
            new ExperimentDefinition(
                "pricing-plus-dashboard",
                Enabled: true,
                new[]
                {
                    new ExperimentVariant("plus-dashboard-in", 1),
                    new ExperimentVariant("plus-dashboard-out", 1),
                }),
        });
        var v = manifest.Bucket("pricing-plus-dashboard", "parent-1");
        Assert.Contains(v.VariantId, new[] { "plus-dashboard-in", "plus-dashboard-out" });
    }
}
