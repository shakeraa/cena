// =============================================================================
// Tests for AI Question Generation Service
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Cena.Admin.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests;

public class AiGenerationServiceTests
{
    private static AiGenerationService CreateService(Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();
        var meterFactory = new TestMeterFactory();
        // Tests don't exercise the CAS-gate path; stub scopeFactory + mode provider.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var gateMode = Substitute.For<ICasGateModeProvider>();
        gateMode.CurrentMode.Returns(CasGateMode.Off);
        return new AiGenerationService(
            NullLogger<AiGenerationService>.Instance, config, meterFactory,
            scopeFactory, gateMode);
    }

    [Fact]
    public async Task GetSettings_ReturnsDefaultConfiguration()
    {
        var service = CreateService();
        var settings = await service.GetSettingsAsync();

        // FIND-arch-005: only Anthropic is supported. Secondary providers
        // (OpenAI, Google, AzureOpenAI) were removed when their stub
        // implementations were deleted.
        Assert.Equal(AiProvider.Anthropic, settings.ActiveProvider);
        Assert.Single(settings.Providers);
        Assert.Contains(settings.Providers, p => p.Provider == AiProvider.Anthropic);
    }

    [Fact]
    public async Task GetSettings_DefaultsAreCorrect()
    {
        var service = CreateService();
        var settings = await service.GetSettingsAsync();

        Assert.Equal("he", settings.Defaults.DefaultLanguage);
        Assert.Equal(3, settings.Defaults.DefaultBloomsLevel);
        Assert.Equal("4 Units", settings.Defaults.DefaultGrade);
        Assert.Equal(5, settings.Defaults.QuestionsPerBatch);
        Assert.True(settings.Defaults.AutoRunQualityGate);
    }

    [Fact]
    public async Task UpdateSettings_UpdatesAnthropicConfig()
    {
        var service = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "sk-ant-test-key",
            ModelId: "claude-sonnet-4-5-20251001",
            Temperature: 0.5f,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var settings = await service.GetSettingsAsync();
        Assert.Equal(AiProvider.Anthropic, settings.ActiveProvider);
        var anthropic = settings.Providers.First(p => p.Provider == AiProvider.Anthropic);
        Assert.True(anthropic.HasApiKey);
        Assert.Equal("claude-sonnet-4-5-20251001", anthropic.ModelId);
    }

    [Fact]
    public async Task UpdateSettings_ChangesDefaults()
    {
        var service = CreateService();
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
        var service = CreateService();
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
        var service = CreateService();
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
    public async Task TestConnection_WithoutKey_ReturnsFalse()
    {
        var service = CreateService();
        var result = await service.TestConnectionAsync(AiProvider.Anthropic);
        Assert.False(result);
    }

    [Fact]
    public async Task TestConnection_WithExplicitKey_ReturnsTrue()
    {
        var service = CreateService();
        await service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "sk-ant-test", ModelId: null, Temperature: null,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var result = await service.TestConnectionAsync(AiProvider.Anthropic);
        Assert.True(result);
    }

    [Fact]
    public async Task TestConnection_AnthropicWithConfigKey_ReturnsTrue()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "sk-ant-test"
        });

        var result = await service.TestConnectionAsync(AiProvider.Anthropic);
        Assert.True(result);
    }

    [Fact]
    public async Task GenerateQuestions_PromptContainsBagrutContext()
    {
        var service = CreateService();
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
