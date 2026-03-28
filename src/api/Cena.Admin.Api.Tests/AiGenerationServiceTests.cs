// =============================================================================
// Tests for AI Question Generation Service
// =============================================================================

using Cena.Admin.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests;

public class AiGenerationServiceTests
{
    private readonly AiGenerationService _service = new(NullLogger<AiGenerationService>.Instance);

    [Fact]
    public async Task GetSettings_ReturnsDefaultConfiguration()
    {
        var settings = await _service.GetSettingsAsync();

        Assert.Equal(AiProvider.Anthropic, settings.ActiveProvider);
        Assert.Equal(4, settings.Providers.Count);
        Assert.Contains(settings.Providers, p => p.Provider == AiProvider.Anthropic);
        Assert.Contains(settings.Providers, p => p.Provider == AiProvider.OpenAI);
        Assert.Contains(settings.Providers, p => p.Provider == AiProvider.Google);
        Assert.Contains(settings.Providers, p => p.Provider == AiProvider.AzureOpenAI);
    }

    [Fact]
    public async Task GetSettings_DefaultsAreCorrect()
    {
        var settings = await _service.GetSettingsAsync();

        Assert.Equal("he", settings.Defaults.DefaultLanguage);
        Assert.Equal(3, settings.Defaults.DefaultBloomsLevel);
        Assert.Equal("4 Units", settings.Defaults.DefaultGrade);
        Assert.Equal(5, settings.Defaults.QuestionsPerBatch);
        Assert.True(settings.Defaults.AutoRunQualityGate);
    }

    [Fact]
    public async Task UpdateSettings_ChangesActiveProvider()
    {
        await _service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.OpenAI,
            ApiKey: "sk-test-key",
            ModelId: "gpt-4o",
            Temperature: 0.5f,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var settings = await _service.GetSettingsAsync();
        Assert.Equal(AiProvider.OpenAI, settings.ActiveProvider);
        var openai = settings.Providers.First(p => p.Provider == AiProvider.OpenAI);
        Assert.True(openai.HasApiKey);
        Assert.Equal("gpt-4o", openai.ModelId);
    }

    [Fact]
    public async Task UpdateSettings_ChangesDefaults()
    {
        await _service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: null, ApiKey: null, ModelId: null,
            Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: "ar",
            DefaultBloomsLevel: 5,
            DefaultGrade: "5 Units",
            QuestionsPerBatch: 10,
            AutoRunQualityGate: false), "test-user");

        var settings = await _service.GetSettingsAsync();
        Assert.Equal("ar", settings.Defaults.DefaultLanguage);
        Assert.Equal(5, settings.Defaults.DefaultBloomsLevel);
        Assert.Equal("5 Units", settings.Defaults.DefaultGrade);
        Assert.Equal(10, settings.Defaults.QuestionsPerBatch);
        Assert.False(settings.Defaults.AutoRunQualityGate);
    }

    [Fact]
    public async Task GenerateQuestions_WithoutApiKey_ReturnsError()
    {
        var result = await _service.GenerateQuestionsAsync(new AiGenerateRequest(
            Subject: "Math", Topic: "Algebra", Grade: "4 Units",
            BloomsLevel: 3, MinDifficulty: 0.5f, MaxDifficulty: 0.5f, Language: "he",
            Context: "Test", ImageBase64: null, FileName: null,
            StyleContext: null, StyleImageBase64: null, StyleFileName: null, Count: 1));

        Assert.False(result.Success);
        Assert.Contains("No API key", result.Error);
    }

    [Fact]
    public async Task GenerateQuestions_WithApiKey_ReturnsMockQuestions()
    {
        // Set API key first
        await _service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic,
            ApiKey: "test-api-key",
            ModelId: null, Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var result = await _service.GenerateQuestionsAsync(new AiGenerateRequest(
            Subject: "Physics", Topic: "Kinematics", Grade: "4 Units",
            BloomsLevel: 3, MinDifficulty: 0.3f, MaxDifficulty: 0.8f, Language: "en",
            Context: "Generate a velocity question", ImageBase64: null, FileName: null,
            StyleContext: null, StyleImageBase64: null, StyleFileName: null, Count: 3));

        Assert.True(result.Success);
        Assert.Equal(3, result.Questions.Count);
        Assert.All(result.Questions, q =>
        {
            Assert.NotEmpty(q.Stem);
            Assert.Equal(4, q.Options.Count);
            Assert.Single(q.Options, o => o.IsCorrect);
        });
        Assert.NotEmpty(result.PromptUsed);
        Assert.NotEmpty(result.ModelUsed);
    }

    [Fact]
    public async Task TestConnection_WithoutKey_ReturnsFalse()
    {
        var result = await _service.TestConnectionAsync(AiProvider.Google);
        Assert.False(result);
    }

    [Fact]
    public async Task TestConnection_WithKey_ReturnsTrue()
    {
        await _service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Google,
            ApiKey: "test-key", ModelId: null, Temperature: null,
            BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var result = await _service.TestConnectionAsync(AiProvider.Google);
        Assert.True(result);
    }

    [Fact]
    public async Task GenerateQuestions_PromptContainsBagrutContext()
    {
        await _service.UpdateSettingsAsync(new UpdateAiSettingsRequest(
            ActiveProvider: AiProvider.Anthropic, ApiKey: "key",
            ModelId: null, Temperature: null, BaseUrl: null, ApiVersion: null,
            DefaultLanguage: null, DefaultBloomsLevel: null,
            DefaultGrade: null, QuestionsPerBatch: null,
            AutoRunQualityGate: null), "test-user");

        var result = await _service.GenerateQuestionsAsync(new AiGenerateRequest(
            Subject: "Chemistry", Topic: "Acids", Grade: "5 Units",
            BloomsLevel: 4, MinDifficulty: 0.7f, MaxDifficulty: 0.7f, Language: "he",
            Context: null, ImageBase64: null, FileName: null,
            StyleContext: null, StyleImageBase64: null, StyleFileName: null, Count: 1));

        Assert.True(result.Success);
        Assert.Contains("Bagrut", result.PromptUsed);
        Assert.Contains("Chemistry", result.PromptUsed);
        Assert.Contains("Analyze", result.PromptUsed); // Bloom level 4
        Assert.Contains("Hebrew", result.PromptUsed);
    }
}
