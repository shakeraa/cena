// =============================================================================
// Tests for /api/admin/ai/settings/model-overrides — both routes (GET + PUT).
//
// Two layers:
//   1. Route smoke (in-process WebApplication, no Marten) — confirms the
//      routes register, gate on AdminOnly, and accept the right body type.
//   2. Handler integration (Marten dev-Postgres, end-to-end) — confirms
//      the PUT mutates AiSettingsDocument, appends an AiSettingsChangedEvent,
//      invalidates the resolver cache, and the GET returns the new value.
// =============================================================================

using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.AiSettings;

public sealed class AiModelOverridesEndpointsRouteSmokeTests
{
    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("api", o =>
            {
                o.PermitLimit = 100;
                o.Window = TimeSpan.FromMinutes(1);
            });
        });

        // The endpoint enumeration triggers RequestDelegate creation which
        // requires every [FromService] parameter on the handler signature
        // to resolve through DI. Register fakes — the route-smoke layer
        // never invokes the handler bodies (the route's metadata is what
        // we assert), so the fakes only need to bind without throwing.
        builder.Services.AddSingleton(NSubstitute.Substitute.For<Marten.IDocumentStore>());
        builder.Services.AddSingleton(NSubstitute.Substitute.For<IModelResolver>());
        builder.Services.AddSingleton(RoutingConfigTaskDefaults.LoadFromYaml(""));
        return builder.Build();
    }

    private static List<RouteEndpoint> EnumerateEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    [Fact]
    public void MapAiModelOverridesEndpoints_RegistersBothRoutes()
    {
        var app = BuildTestApp();
        app.MapAiModelOverridesEndpoints();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .ToHashSet();

        Assert.Contains("/api/admin/ai/settings/model-overrides", patterns);
        Assert.Contains("/api/admin/ai/settings/model-overrides/{taskName}", patterns);
    }

    [Fact]
    public void BothRoutes_RequireAdminOnly()
    {
        var app = BuildTestApp();
        app.MapAiModelOverridesEndpoints();

        foreach (var endpoint in EnumerateEndpoints(app)
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/admin/ai/settings/model-overrides") == true))
        {
            var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
            Assert.NotEmpty(authAttrs);
            Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.AdminOnly);
        }
    }
}

public sealed class AiModelOverridesEndpointsIntegrationTests : IAsyncLifetime
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
    private RoutingConfigTaskDefaults _yaml = null!;

    public Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "model_overrides_test_" + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Schema.For<AiSettingsDocument>().Identity(d => d.Id);
            // Match production MartenConfiguration: string stream ids so the
            // singleton AiSettings stream key ("ai-settings-singleton") works.
            opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(AiSettingsChangedEvent));
        });
        _yaml = RoutingConfigTaskDefaults.LoadFromYaml(TestYaml);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    private ModelResolver CreateResolver() =>
        new(_store, _yaml, NullLogger<ModelResolver>.Instance);

    private static System.Security.Claims.ClaimsPrincipal MakeUser(string sub) =>
        new(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("sub", sub) }, "test"));

    private static Microsoft.AspNetCore.Http.HttpContext MakeHttpContext(string changedBy)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = MakeUser(changedBy) };
        return ctx;
    }

    [Fact]
    public async Task GetHandler_ReturnsTasks_SupportedModels_AndGlobalDefault()
    {
        var resolver = CreateResolver();
        var result = await AiModelOverridesEndpoints.HandleGetAsync(
            resolver, _store, _yaml, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result);
        var resp = Assert.IsType<ModelOverridesResponse>(ok.Value);

        Assert.NotEmpty(resp.SupportedModels);
        Assert.Equal("claude-sonnet-4-6", resp.GlobalDefaultModelId);

        // Tasks projection includes every YAML task.
        var byTask = resp.Tasks.ToDictionary(t => t.Task);
        Assert.Contains("quality_gate", byTask.Keys);
        Assert.Equal("claude-haiku-4-5-20251001", byTask["quality_gate"].CurrentModelId);
        Assert.Equal("routing-config-task-default", byTask["quality_gate"].Source);
        Assert.False(byTask["quality_gate"].IsOverridden);
    }

    [Fact]
    public async Task PutHandler_SetsOverride_AppendsAuditEvent_AndInvalidatesCache()
    {
        var resolver = CreateResolver();

        // Prime the resolver cache with the routing-config-default value.
        Assert.Equal("claude-haiku-4-5-20251001",
            await resolver.ResolveModelForTaskAsync("quality_gate"));

        var result = await AiModelOverridesEndpoints.HandlePutAsync(
            taskName: "quality_gate",
            request: new SetModelOverrideRequest("claude-sonnet-4-6"),
            httpContext: MakeHttpContext("user-tamar"),
            documentStore: _store,
            resolver: resolver,
            yamlDefaults: _yaml,
            logger: NullLogger<ModelResolver>.Instance,
            ct: CancellationToken.None);

        // 200 OK, body has the post-change current model.
        var ok = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result);
        Assert.NotNull(ok.Value);

        // The doc was mutated.
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<AiSettingsDocument>(AiSettingsDocument.SingletonId);
        Assert.NotNull(doc);
        Assert.True(doc!.ModelOverridesByTask.TryGetValue("quality_gate", out var modelId));
        Assert.Equal("claude-sonnet-4-6", modelId);
        Assert.Equal("user-tamar", doc.ModelOverridesLastChangedBy);
        Assert.NotNull(doc.ModelOverridesLastChangedAt);

        // The event was appended on the singleton's stream.
        var events = await session.Events.FetchStreamAsync(AiSettingsDocument.SingletonId);
        var changeEvent = events
            .Select(e => e.Data)
            .OfType<AiSettingsChangedEvent>()
            .Single();
        Assert.Equal("quality_gate", changeEvent.TaskName);
        Assert.Null(changeEvent.OldModelId);
        Assert.Equal("claude-sonnet-4-6", changeEvent.NewModelId);
        Assert.Equal("user-tamar", changeEvent.ChangedBy);

        // Cache was invalidated — the resolver returns the new value
        // immediately, not the cached pre-change Haiku.
        Assert.Equal("claude-sonnet-4-6",
            await resolver.ResolveModelForTaskAsync("quality_gate"));
    }

    [Fact]
    public async Task PutHandler_ClearOverride_RemovesEntry_AppendsClearEvent()
    {
        var resolver = CreateResolver();

        // Set, then clear.
        await AiModelOverridesEndpoints.HandlePutAsync(
            "quality_gate", new SetModelOverrideRequest("claude-sonnet-4-6"),
            MakeHttpContext("user-set"),
            _store, resolver, _yaml, NullLogger<ModelResolver>.Instance, CancellationToken.None);

        var result = await AiModelOverridesEndpoints.HandlePutAsync(
            "quality_gate", new SetModelOverrideRequest(null),
            MakeHttpContext("user-clear"),
            _store, resolver, _yaml, NullLogger<ModelResolver>.Instance, CancellationToken.None);

        var ok = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result);

        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<AiSettingsDocument>(AiSettingsDocument.SingletonId);
        Assert.NotNull(doc);
        // The override was removed → resolver falls back to routing-config.
        Assert.False(doc!.ModelOverridesByTask.ContainsKey("quality_gate"));

        // Two events on the stream: set then clear.
        var events = (await session.Events.FetchStreamAsync(AiSettingsDocument.SingletonId))
            .Select(e => e.Data).OfType<AiSettingsChangedEvent>().ToList();
        Assert.Equal(2, events.Count);
        var clearEvent = events[1];
        Assert.Equal("quality_gate", clearEvent.TaskName);
        Assert.Equal("claude-sonnet-4-6", clearEvent.OldModelId);
        Assert.Null(clearEvent.NewModelId);
        Assert.Equal("user-clear", clearEvent.ChangedBy);

        // Resolver re-reads → routing-config-task-default Haiku.
        Assert.Equal("claude-haiku-4-5-20251001",
            await resolver.ResolveModelForTaskAsync("quality_gate"));
    }

    [Fact]
    public async Task PutHandler_UnsupportedModelId_Returns400()
    {
        var resolver = CreateResolver();
        var result = await AiModelOverridesEndpoints.HandlePutAsync(
            "quality_gate",
            new SetModelOverrideRequest("gpt-4-bogus"),
            MakeHttpContext("u"),
            _store, resolver, _yaml, NullLogger<ModelResolver>.Instance, CancellationToken.None);

        var bad = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task PutHandler_UnknownTaskName_Returns400()
    {
        var resolver = CreateResolver();
        var result = await AiModelOverridesEndpoints.HandlePutAsync(
            "task_that_does_not_exist",
            new SetModelOverrideRequest("claude-sonnet-4-6"),
            MakeHttpContext("u"),
            _store, resolver, _yaml, NullLogger<ModelResolver>.Instance, CancellationToken.None);

        var bad = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }
}
