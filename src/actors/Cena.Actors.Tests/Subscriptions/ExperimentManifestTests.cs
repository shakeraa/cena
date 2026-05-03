// =============================================================================
// Cena Platform — ExperimentManifest + bucketer + resolver tests (EPIC-PRR-I PRR-332)
// =============================================================================

using Cena.Actors.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void Manifest_get_returns_null_for_unknown_experiment()
    {
        var manifest = new ExperimentManifest(Array.Empty<ExperimentDefinition>());
        Assert.Null(manifest.Get("missing"));
    }

    [Fact]
    public void Manifest_get_returns_definition_for_known_experiment()
    {
        var def = new ExperimentDefinition(
            "known", Enabled: true,
            new[] { new ExperimentVariant("control", 1) });
        var manifest = new ExperimentManifest(new[] { def });
        Assert.Same(def, manifest.Get("known"));
    }
}

public class BucketHasherTests
{
    [Fact]
    public void BucketFor_is_deterministic_same_input_same_bucket()
    {
        var b1 = BucketHasher.BucketFor("parent-42", "pricing-plus-dashboard");
        var b2 = BucketHasher.BucketFor("parent-42", "pricing-plus-dashboard");
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void BucketFor_different_experiments_give_different_buckets_for_same_user()
    {
        // Across many different experiment ids the same user must NOT collapse
        // onto a single bucket — otherwise a user "always in control" across
        // every experiment is a non-randomisation bug. Count the distinct
        // buckets over 50 different experiments.
        var buckets = new HashSet<int>();
        for (var i = 0; i < 50; i++)
        {
            buckets.Add(BucketHasher.BucketFor("parent-42", $"experiment-{i}"));
        }
        // Expect most of the 50 to be distinct buckets; allow a few collisions.
        Assert.True(buckets.Count >= 35,
            $"expected ≥35 distinct buckets across 50 experiments, got {buckets.Count}");
    }

    [Fact]
    public void BucketFor_distribution_is_approximately_uniform()
    {
        // 1000 deterministic parent ids across 10 buckets; loose guardrail:
        // no bucket should receive more than 3× the expected count (100).
        var counts = new int[10];
        for (var i = 0; i < 1000; i++)
        {
            var b = BucketHasher.BucketFor($"parent-{i}", "distribution-test", buckets: 10);
            counts[b]++;
        }
        foreach (var c in counts)
        {
            Assert.InRange(c, 33, 300); // ≥⅓ expected, ≤3× expected.
        }
    }

    [Fact]
    public void BucketFor_rejects_non_positive_bucket_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BucketHasher.BucketFor("s", "e", buckets: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BucketHasher.BucketFor("s", "e", buckets: -1));
    }

    [Fact]
    public void SlotFor_respects_weights_across_many_subjects()
    {
        // 70/30 split across 10k subjects — ~7000 in [0, 7), ~3000 in [7, 10).
        var belowSeven = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var slot = BucketHasher.SlotFor($"parent-{i}", "weights-test", totalWeight: 10);
            if (slot < 7) belowSeven++;
        }
        Assert.InRange(belowSeven, 6_500, 7_500);
    }

    [Fact]
    public void SlotFor_rejects_non_positive_total_weight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BucketHasher.SlotFor("s", "e", totalWeight: 0));
    }
}

public class ExperimentVariantResolverTests
{
    [Fact]
    public void Active_experiment_resolves_to_one_of_the_variant_ids()
    {
        var resolver = BuildResolver(
            new ExperimentDefinition(
                "pricing-plus-dashboard", Enabled: true,
                new[]
                {
                    new ExperimentVariant("plus-dashboard-in", 1),
                    new ExperimentVariant("plus-dashboard-out", 1),
                }));
        var id = resolver.ResolveVariantId("parent-1", "pricing-plus-dashboard");
        Assert.Contains(id, new[] { "plus-dashboard-in", "plus-dashboard-out" });
    }

    [Fact]
    public void Killswitched_experiment_returns_null()
    {
        // Contract: a killed experiment returns null so the consumer does NOT
        // log a cohort tag (PRR-330 unit-economics would otherwise pollute).
        var resolver = BuildResolver(
            new ExperimentDefinition(
                "killed", Enabled: false,
                new[]
                {
                    new ExperimentVariant("control", 1),
                    new ExperimentVariant("treatment", 1),
                }));
        Assert.Null(resolver.ResolveVariantId("parent-1", "killed"));
    }

    [Fact]
    public void Unknown_experiment_returns_null()
    {
        var resolver = BuildResolver();
        Assert.Null(resolver.ResolveVariantId("parent-1", "not-registered"));
    }

    [Fact]
    public void Resolver_is_deterministic_for_same_subject()
    {
        var resolver = BuildResolver(
            new ExperimentDefinition(
                "detr", Enabled: true,
                new[]
                {
                    new ExperimentVariant("A", 1),
                    new ExperimentVariant("B", 1),
                }));
        var a = resolver.ResolveVariantId("parent-stable", "detr");
        var b = resolver.ResolveVariantId("parent-stable", "detr");
        Assert.Equal(a, b);
        Assert.NotNull(a);
    }

    [Fact]
    public void Empty_variant_list_returns_null()
    {
        // Defensive: an experiment registered with zero variants is a
        // misconfiguration; resolver should return null rather than a
        // sentinel "control" that would mislead the consumer.
        var resolver = BuildResolver(
            new ExperimentDefinition(
                "empty", Enabled: true,
                Array.Empty<ExperimentVariant>()));
        Assert.Null(resolver.ResolveVariantId("parent-1", "empty"));
    }

    [Fact]
    public void DI_helper_registers_resolver_and_manifest()
    {
        var services = new ServiceCollection();
        services.AddExperiments(new[]
        {
            new ExperimentDefinition(
                "dev-smoke", Enabled: true,
                new[] { new ExperimentVariant("control", 1) }),
        });
        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<IExperimentVariantResolver>();
        var manifest = sp.GetRequiredService<ExperimentManifest>();

        Assert.Equal("control", resolver.ResolveVariantId("parent-1", "dev-smoke"));
        Assert.Contains("dev-smoke", manifest.Ids);
    }

    [Fact]
    public void DI_helper_default_registers_empty_manifest()
    {
        // Callers that invoke AddExperiments() without a seed get a
        // harness whose resolver says null for every experiment — the
        // honest "no A/B running here" default. Production-grade; not a stub.
        var services = new ServiceCollection();
        services.AddExperiments();
        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<IExperimentVariantResolver>();
        Assert.Null(resolver.ResolveVariantId("parent-1", "anything"));
    }

    private static IExperimentVariantResolver BuildResolver(params ExperimentDefinition[] definitions)
    {
        var manifest = new ExperimentManifest(definitions);
        return new ManifestExperimentVariantResolver(manifest);
    }
}
