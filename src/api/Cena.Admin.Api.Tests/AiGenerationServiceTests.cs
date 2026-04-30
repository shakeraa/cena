// =============================================================================
// Tests for AI Question Generation Service
//
// Per v2 §6 / PRR-313 lesson: tests that exercise Marten persistence run
// against the dev cena-postgres, not against an NSubstitute stub of
// IDocumentSession. The previous mock-based fixture ran into Marten v8's
// params-array Store<T>(params T[]) overload — NSubstitute recorded the
// argument as AiSettingsDocument[1], not the bare doc, and call.Arg<T>()
// threw ArgumentNotFoundException. The pattern here mirrors
// MockExamRunServiceTests.cs (Cena.Actors.Tests) and
// RightToErasureEndToEndTests (Cena.Infrastructure.Tests).
//
// Each test class instance gets its own Marten schema, so parallel runs
// and per-test isolation are both safe. Cipher + probe stay as in-process
// fakes — those interfaces are entirely under our control with no
// params-overload weirdness, and a deterministic FakeApiKeyCipher lets us
// assert "value is encrypted, not plaintext" without burning AES on every
// test (HkdfApiKeyCipherTests covers the real cipher).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Cena.Admin.Api;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using JasperFx;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests;

public sealed class AiGenerationServiceTests : IAsyncLifetime
{
    // dev compose maps cena-postgres:5432 → host:5433. CI runs the same
    // compose stack so this is portable. Connection string mirrors
    // MockExamRunServiceTests so future readers see one shared pattern.
    private const string ConnectionString =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private DocumentStore _store = null!;

    public Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            // Per-instance random schema → xUnit creates a new test-class
            // instance per [Fact], so each test runs against a clean
            // schema with zero risk of cross-test bleed (the singleton id
            // "ai-settings-singleton" would collide otherwise).
            opts.DatabaseSchemaName = "ai_settings_test_" + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Schema.For<AiSettingsDocument>().Identity(d => d.Id);
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deterministic cipher for tests — wraps plaintext in a recognisable
    /// prefix so we can assert "this is encrypted" vs raw plaintext, but
    /// without burning real AES-GCM round-trips on every test.
    /// HkdfApiKeyCipherTests covers the real AES-GCM path.
    /// </summary>
    private sealed class FakeApiKeyCipher : IApiKeyCipher
    {
        private const string Prefix = "fake-cipher:";
        public string EncryptToWire(string plaintext)
            => string.IsNullOrEmpty(plaintext) ? "" : Prefix + plaintext;
        public bool TryDecryptFromWire(string wire, out string plaintext)
        {
            plaintext = "";
            if (string.IsNullOrEmpty(wire) || !wire.StartsWith(Prefix)) return false;
            plaintext = wire.Substring(Prefix.Length);
            return true;
        }
    }

    /// <summary>
    /// Probe that returns whatever ConnectionTestResult the test sets and
    /// records the apiKey + modelId it received so the test can assert the
    /// service decrypted the persisted ciphertext before dispatch.
    /// </summary>
    private sealed class FakeProbe : IAnthropicConnectionProbe
    {
        public ConnectionTestResult NextResult { get; set; } =
            ConnectionTestResult.Ok("fake probe ok");
        public string? LastApiKey { get; private set; }
        public string? LastModelId { get; private set; }

        public Task<ConnectionTestResult> ProbeAsync(
            string apiKey, string modelId, string? baseUrl, CancellationToken ct = default)
        {
            LastApiKey = apiKey;
            LastModelId = modelId;
            return Task.FromResult(NextResult);
        }
    }

    private (AiGenerationService Service, FakeProbe Probe) CreateService(
        Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();
        var meterFactory = new TestMeterFactory();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var gateMode = Substitute.For<ICasGateModeProvider>();
        gateMode.CurrentMode.Returns(CasGateMode.Off);
        var probe = new FakeProbe();
        var service = new AiGenerationService(
            NullLogger<AiGenerationService>.Instance, config, meterFactory,
            scopeFactory, gateMode, NullLlmCostMetric.Instance,
            _store, new FakeApiKeyCipher(), probe);
        return (service, probe);
    }

    private async Task<AiSettingsDocument?> LoadPersistedDocAsync()
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<AiSettingsDocument>(AiSettingsDocument.SingletonId);
    }

    [Fact]
    public async Task GetSettings_ReturnsDefaultConfiguration()
    {
        var (service, _) = CreateService();
        var settings = await service.GetSettingsAsync();

        // FIND-arch-005: only Anthropic is supported.
        Assert.Equal(AiProvider.Anthropic, settings.ActiveProvider);
        Assert.Single(settings.Providers);
        Assert.Contains(settings.Providers, p => p.Provider == AiProvider.Anthropic);
    }

    [Fact]
    public async Task GetSettings_DefaultsAreCorrect()
    {
        var (service, _) = CreateService();
        var settings = await service.GetSettingsAsync();

        Assert.Equal("he", settings.Defaults.DefaultLanguage);
        Assert.Equal(3, settings.Defaults.DefaultBloomsLevel);
        Assert.Equal("4 Units", settings.Defaults.DefaultGrade);
        Assert.Equal(5, settings.Defaults.QuestionsPerBatch);
        Assert.True(settings.Defaults.AutoRunQualityGate);
    }

    [Fact]
    public async Task UpdateSettings_PersistsToDocumentStore_AndEncryptsApiKey()
    {
        var (service, _) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "sk-ant-test-key",
            ModelId: "claude-sonnet-4-6",
            Temperature: 0.5f,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        // Persisted to Marten — verify directly via a fresh QuerySession,
        // not via the service's hot cache.
        var doc = await LoadPersistedDocAsync();
        Assert.NotNull(doc);
        Assert.Equal("claude-sonnet-4-6", doc!.AnthropicModelId);
        Assert.Equal("test-user", doc.UpdatedBy);
        // The service must have routed the plaintext through
        // IApiKeyCipher.EncryptToWire before persisting. Assert against the
        // FakeApiKeyCipher's deterministic wire form ("fake-cipher:" + pt)
        // so this test stays a unit test of the service's plumbing.
        // The real "ciphertext is not readable as plaintext" property is
        // an AES-GCM invariant covered by HkdfApiKeyCipherTests.
        Assert.Equal("fake-cipher:sk-ant-test-key", doc.AnthropicApiKeyCipher);

        // Round-trips back via the GET projection
        var settings = await service.GetSettingsAsync();
        var anthropic = settings.Providers.First(p => p.Provider == AiProvider.Anthropic);
        Assert.True(anthropic.HasApiKey);
        Assert.Equal("claude-sonnet-4-6", anthropic.ModelId);
    }

    [Fact]
    public async Task UpdateSettings_OmittingApiKey_PreservesExistingKey()
    {
        var (service, _) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic, ApiKey: "sk-ant-original",
            ModelId: null, Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "u1");

        var firstCipher = (await LoadPersistedDocAsync())!.AnthropicApiKeyCipher;

        // Change only the model. The SPA sends apiKey: undefined when the
        // user is not editing the key field — backend must keep the
        // existing cipher byte-for-byte.
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: null, ApiKey: null,
            ModelId: "claude-haiku-4-5-20251001",
            Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "u2");

        var doc = await LoadPersistedDocAsync();
        Assert.NotNull(doc);
        Assert.Equal("claude-haiku-4-5-20251001", doc!.AnthropicModelId);
        Assert.Equal(firstCipher, doc.AnthropicApiKeyCipher);
    }

    [Fact]
    public async Task UpdateSettings_ChangesDefaults()
    {
        var (service, _) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: null, ApiKey: null, ModelId: null,
            Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: "ar",
            DefaultBloomsLevel: 5,
            DefaultGrade: "5 Units",
            QuestionsPerBatch: 10,
            AutoRunQualityGate: false), "test-user");

        var settings = await service.GetSettingsAsync();
        Assert.Equal("ar", settings.Defaults.DefaultLanguage);
        Assert.Equal(5, settings.Defaults.DefaultBloomsLevel);
        Assert.Equal("5 Units", settings.Defaults.DefaultGrade);
        Assert.Equal(10, settings.Defaults.QuestionsPerBatch);
        Assert.False(settings.Defaults.AutoRunQualityGate);
    }

    [Fact]
    public async Task GenerateQuestions_WithoutApiKey_ReturnsError()
    {
        var (service, _) = CreateService();
        var result = await service.GenerateQuestionsAsync(new AiGenerateRequest(
            Subject: "Math", Topic: "Algebra", Grade: "4 Units",
            BloomsLevel: 3, MinDifficulty: 0.5f, MaxDifficulty: 0.5f, Language: "he",
            Context: "Test", ImageBase64: null, FileName: null,
            StyleContext: null, StyleImageBase64: null, StyleFileName: null, Count: 1));

        Assert.False(result.Success);
        Assert.Contains("No API key", result.Error);
    }

    [Fact]
    public async Task GenerateQuestions_WithFakeApiKey_FailsWithApiError()
    {
        // With a real SDK, a fake key will fail on the API call — not return mocks
        var (service, _) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "test-api-key",
            ModelId: null, Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var result = await service.GenerateQuestionsAsync(new AiGenerateRequest(
            Subject: "Physics", Topic: "Kinematics", Grade: "4 Units",
            BloomsLevel: 3, MinDifficulty: 0.3f, MaxDifficulty: 0.8f, Language: "en",
            Context: "Generate a velocity question", ImageBase64: null, FileName: null,
            StyleContext: null, StyleImageBase64: null, StyleFileName: null, Count: 3));

        // Expect failure because the fake API key won't authenticate
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task TestConnection_WithoutKey_ReturnsConfigMissingKey()
    {
        var (service, probe) = CreateService();
        var result = await service.TestConnectionAsync(AiProvider.Anthropic);
        Assert.False(result.Success);
        Assert.Equal("CONFIG_MISSING_KEY", result.Details);
        // Probe must NOT be called when no key is configured.
        Assert.Null(probe.LastApiKey);
    }

    [Fact]
    public async Task TestConnection_WithPersistedKey_DispatchesToProbeWithDecryptedKey()
    {
        var (service, probe) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "sk-ant-real", ModelId: null, Temperature: null,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        probe.NextResult = ConnectionTestResult.Ok("ok");
        var result = await service.TestConnectionAsync(AiProvider.Anthropic);

        Assert.True(result.Success);
        // Probe receives decrypted plaintext, not the cipher blob
        Assert.Equal("sk-ant-real", probe.LastApiKey);
    }

    [Fact]
    public async Task TestConnection_PropagatesProbeFailureCategory()
    {
        var (service, probe) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "sk-ant-bad", ModelId: null, Temperature: null,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        probe.NextResult = ConnectionTestResult.Fail("Invalid API key", "AUTH_FAILED");
        var result = await service.TestConnectionAsync(AiProvider.Anthropic);

        Assert.False(result.Success);
        Assert.Equal("Invalid API key", result.Error);
        Assert.Equal("AUTH_FAILED", result.Details);
    }

    [Fact]
    public async Task TestConnection_ConfigKeyOnly_StillProbes()
    {
        // No persisted key; only IConfiguration provides it. The probe must
        // still be dispatched so the operator can verify the env-var-supplied
        // key works.
        var (service, probe) = CreateService(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "sk-ant-from-env"
        });

        probe.NextResult = ConnectionTestResult.Ok("ok");
        var result = await service.TestConnectionAsync(AiProvider.Anthropic);

        Assert.True(result.Success);
        Assert.Equal("sk-ant-from-env", probe.LastApiKey);
    }

    [Fact]
    public async Task GenerateQuestions_PromptContainsBagrutContext()
    {
        var (service, _) = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic, ApiKey: "key",
            ModelId: null, Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var result = await service.GenerateQuestionsAsync(new AiGenerateRequest(
            Subject: "Chemistry", Topic: "Acids", Grade: "5 Units",
            BloomsLevel: 4, MinDifficulty: 0.7f, MaxDifficulty: 0.7f, Language: "he",
            Context: null, ImageBase64: null, FileName: null,
            StyleContext: null, StyleImageBase64: null, StyleFileName: null, Count: 1));

        // It will fail (fake key) but prompt should still be populated
        Assert.NotEmpty(result.PromptUsed);
        Assert.Contains("Bagrut", result.PromptUsed);
        Assert.Contains("Chemistry", result.PromptUsed);
        Assert.Contains("Analyze", result.PromptUsed); // Bloom level 4
        Assert.Contains("Hebrew", result.PromptUsed);
    }

    /// <summary>Minimal IMeterFactory implementation for tests.</summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var m in _meters) m.Dispose();
        }
    }
}
