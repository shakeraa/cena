// =============================================================================
// Tests for ModelResolver — the resolution chain (override → routing-config-task
// → routing-config-global → throw) and the cache-invalidation contract.
//
// Uses the same Marten dev-Postgres pattern as AiGenerationServiceTests:
// random schema per test instance, AutoCreate.All so the singleton doc
// table comes up on first save.
// =============================================================================

using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.AiSettings;

/// <summary>
/// Tiny TimeProvider shim — same shape used by OcrEnhancementCacheTests
/// + PostReflectionMasteryServiceTests; avoids pulling
/// Microsoft.Extensions.TimeProvider.Testing into Cena.Admin.Api.Tests.
/// </summary>
internal sealed class TestableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    public TestableTimeProvider(DateTimeOffset start) { _now = start; }
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) { _now = _now + delta; }
}

public sealed class ModelResolverTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private const string TestYaml = """
        default_model_by_task:
          quality_gate:        "claude-haiku-4-5-20251001"
          concept_extraction:  "claude-haiku-4-5-20251001"
          question_generation: "claude-sonnet-4-6"
        global_default_model_id: "claude-sonnet-4-6"
        """;

    private DocumentStore _store = null!;
    private RoutingConfigTaskDefaults _yamlDefaults = null!;

    public Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "model_resolver_test_" + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Schema.For<AiSettingsDocument>().Identity(d => d.Id);
        });
        _yamlDefaults = RoutingConfigTaskDefaults.LoadFromYaml(TestYaml);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    private ModelResolver CreateResolver(TimeProvider? clock = null) =>
        new(_store, _yamlDefaults, NullLogger<ModelResolver>.Instance, clock);

    private async Task SeedDocAsync(Action<AiSettingsDocument> mutate)
    {
        await using var session = _store.LightweightSession();
        var doc = new AiSettingsDocument { Id = AiSettingsDocument.SingletonId };
        mutate(doc);
        session.Store(doc);
        await session.SaveChangesAsync();
    }

    // ── 1. Resolution chain ──────────────────────────────────────────────

    [Fact]
    public async Task Resolve_OverrideTier_TakesPrecedenceOverRoutingConfig()
    {
        await SeedDocAsync(d => d.ModelOverridesByTask["quality_gate"] = "claude-opus-4-7");

        var resolver = CreateResolver();
        var modelId = await resolver.ResolveModelForTaskAsync("quality_gate");

        // Override wins, even though routing-config pins quality_gate to Haiku.
        Assert.Equal("claude-opus-4-7", modelId);
    }

    [Fact]
    public async Task Resolve_NoOverride_FallsThroughToRoutingConfigTaskDefault()
    {
        // Empty override map — routing-config-task-default tier kicks in.
        var resolver = CreateResolver();
        var modelId = await resolver.ResolveModelForTaskAsync("quality_gate");

        Assert.Equal("claude-haiku-4-5-20251001", modelId);
    }

    [Fact]
    public async Task Resolve_NoOverride_NoTaskDefault_FallsThroughToGlobalDefault()
    {
        // "ad_hoc" is not in TestYaml's per-task map; resolver should
        // fall through to global_default_model_id (Sonnet).
        var resolver = CreateResolver();
        var modelId = await resolver.ResolveModelForTaskAsync("ad_hoc");

        Assert.Equal("claude-sonnet-4-6", modelId);
    }

    [Fact]
    public async Task Resolve_NoMatchAtAnyTier_Throws()
    {
        var emptyYaml = RoutingConfigTaskDefaults.LoadFromYaml("");
        var resolver = new ModelResolver(_store, emptyYaml, NullLogger<ModelResolver>.Instance);

        await Assert.ThrowsAsync<ModelNotConfiguredException>(
            () => resolver.ResolveModelForTaskAsync("any_task"));
    }

    [Fact]
    public async Task Resolve_BlankTaskName_Throws()
    {
        var resolver = CreateResolver();
        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveModelForTaskAsync(""));
    }

    // ── 2. Cache invalidation on AiSettingsDocument change ───────────────

    [Fact]
    public async Task Resolve_CachesDocFor60Seconds_ReReadsAfterTtl()
    {
        await SeedDocAsync(d => d.ModelOverridesByTask["quality_gate"] = "claude-opus-4-7");

        var clock = new TestableTimeProvider(DateTimeOffset.UtcNow);
        var resolver = CreateResolver(clock);

        // First call: hits Marten, returns Opus override.
        Assert.Equal("claude-opus-4-7", await resolver.ResolveModelForTaskAsync("quality_gate"));

        // Mutate the doc OUTSIDE the resolver (simulates a different
        // host instance writing). Without invalidation, the resolver's
        // cache returns the stale value until TTL.
        await SeedDocAsync(d =>
        {
            d.ModelOverridesByTask.Clear();
            d.ModelOverridesByTask["quality_gate"] = "claude-sonnet-4-5";
        });

        // Second call: still within 60s window, still serving cached doc.
        Assert.Equal("claude-opus-4-7", await resolver.ResolveModelForTaskAsync("quality_gate"));

        // Advance clock past TTL — next call must refresh from Marten.
        clock.Advance(ModelResolver.CacheTtl + TimeSpan.FromSeconds(1));
        Assert.Equal("claude-sonnet-4-5", await resolver.ResolveModelForTaskAsync("quality_gate"));
    }

    [Fact]
    public async Task Invalidate_DropsCachedDoc_NextCallReReadsImmediately()
    {
        // Same scenario as above but uses Invalidate() to evict — proves
        // the PUT endpoint can shortcut the 60s TTL window.
        await SeedDocAsync(d => d.ModelOverridesByTask["quality_gate"] = "claude-opus-4-7");

        var resolver = CreateResolver();
        Assert.Equal("claude-opus-4-7", await resolver.ResolveModelForTaskAsync("quality_gate"));

        await SeedDocAsync(d =>
        {
            d.ModelOverridesByTask.Clear();
            d.ModelOverridesByTask["quality_gate"] = "claude-haiku-4-5-20251001";
        });

        // Without Invalidate(), the resolver still serves the cached
        // Opus value.
        Assert.Equal("claude-opus-4-7", await resolver.ResolveModelForTaskAsync("quality_gate"));

        // After Invalidate(), the next call refreshes to Haiku.
        resolver.Invalidate();
        Assert.Equal("claude-haiku-4-5-20251001", await resolver.ResolveModelForTaskAsync("quality_gate"));
    }

    // ── 3. Snapshot for the GET endpoint ─────────────────────────────────

    [Fact]
    public async Task SnapshotAsync_IncludesEveryYamlTask_PlusOverridesNotInYaml()
    {
        await SeedDocAsync(d => d.ModelOverridesByTask["quality_gate"] = "claude-opus-4-7");

        var resolver = CreateResolver();
        var snapshot = await resolver.SnapshotAsync();

        var byTask = snapshot.ToDictionary(r => r.Task);
        Assert.Contains("quality_gate", byTask.Keys);
        Assert.Contains("concept_extraction", byTask.Keys);
        Assert.Contains("question_generation", byTask.Keys);

        // quality_gate has an override → source=override, isOverridden=true,
        // overrideModelId=opus, currentModelId=opus.
        var qg = byTask["quality_gate"];
        Assert.True(qg.IsOverridden);
        Assert.Equal("override", qg.Source);
        Assert.Equal("claude-opus-4-7", qg.CurrentModelId);
        Assert.Equal("claude-opus-4-7", qg.OverrideModelId);

        // concept_extraction has no override → source=routing-config-task-default,
        // isOverridden=false, currentModelId=Haiku.
        var ce = byTask["concept_extraction"];
        Assert.False(ce.IsOverridden);
        Assert.Equal("routing-config-task-default", ce.Source);
        Assert.Equal("claude-haiku-4-5-20251001", ce.CurrentModelId);
        Assert.Null(ce.OverrideModelId);
    }
}
