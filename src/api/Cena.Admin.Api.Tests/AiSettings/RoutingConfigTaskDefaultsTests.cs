// =============================================================================
// Tests for RoutingConfigTaskDefaults — YAML parsing of § default_model_by_task:
// and § global_default_model_id sections of routing-config.yaml.
//
// Runs entirely in-process (no Marten, no Postgres) — the loader takes
// raw YAML strings, and the YamlDotNet parse is the only side effect.
// =============================================================================

using Cena.Admin.Api.AiSettings;

namespace Cena.Admin.Api.Tests.AiSettings;

public sealed class RoutingConfigTaskDefaultsTests
{
    private const string MinimalYaml = """
        default_model_by_task:
          quality_gate:        "claude-haiku-4-5-20251001"
          question_generation: "claude-sonnet-4-6"

        global_default_model_id: "claude-sonnet-4-6"
        """;

    [Fact]
    public void LoadFromYaml_ResolvesPerTaskDefault()
    {
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml(MinimalYaml);

        var ok = defaults.TryResolveDefault("quality_gate", out var modelId, out var source);

        Assert.True(ok);
        Assert.Equal("claude-haiku-4-5-20251001", modelId);
        Assert.Equal("routing-config-task-default", source);
    }

    [Fact]
    public void LoadFromYaml_FallsThroughToGlobalDefault_WhenTaskMissing()
    {
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml(MinimalYaml);

        var ok = defaults.TryResolveDefault("unknown_task", out var modelId, out var source);

        Assert.True(ok);
        Assert.Equal("claude-sonnet-4-6", modelId);
        Assert.Equal("routing-config-global-default", source);
    }

    [Fact]
    public void LoadFromYaml_ReturnsFalse_WhenNoDefaultsAtAll()
    {
        // YAML present but neither section configured — TryResolveDefault
        // must return false so the caller can decide whether to throw.
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml("""
            other_section:
              foo: bar
            """);

        var ok = defaults.TryResolveDefault("anything", out var modelId, out _);

        Assert.False(ok);
        Assert.Equal("", modelId);
    }

    [Fact]
    public void LoadFromYaml_OnlyGlobalSet_ReturnsGlobalForUnknownTask()
    {
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml(
            "global_default_model_id: \"claude-haiku-4-5-20251001\"");

        var ok = defaults.TryResolveDefault("anything", out var modelId, out var source);

        Assert.True(ok);
        Assert.Equal("claude-haiku-4-5-20251001", modelId);
        Assert.Equal("routing-config-global-default", source);
    }

    [Fact]
    public void LoadFromYaml_KnownTaskNamesProjection()
    {
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml(MinimalYaml);

        Assert.Contains("quality_gate", defaults.KnownTaskNames);
        Assert.Contains("question_generation", defaults.KnownTaskNames);
        Assert.Equal(2, defaults.KnownTaskNames.Count);
    }

    [Fact]
    public void LoadFromYaml_GlobalDefaultModelIdProjection()
    {
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml(MinimalYaml);
        Assert.Equal("claude-sonnet-4-6", defaults.GlobalDefaultModelId);
    }

    [Fact]
    public void TryResolveDefault_BlankTaskName_ReturnsFalse()
    {
        var defaults = RoutingConfigTaskDefaults.LoadFromYaml(MinimalYaml);

        Assert.False(defaults.TryResolveDefault("", out _, out _));
        Assert.False(defaults.TryResolveDefault("   ", out _, out _));
    }

    [Fact]
    public void LoadFromFile_FailsLoud_OnMissingFile()
    {
        var bogusPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.yaml");
        var ex = Assert.Throws<FileNotFoundException>(
            () => RoutingConfigTaskDefaults.LoadFromFile(bogusPath));

        Assert.Contains("ModelResolver requires this file", ex.Message);
    }
}
