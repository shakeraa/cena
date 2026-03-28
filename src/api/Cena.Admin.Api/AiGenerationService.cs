// =============================================================================
// Cena Platform — AI Question Generation Service
// Provider-agnostic abstraction for LLM-based question generation.
// Supports Anthropic (Claude), OpenAI, Google (Gemini), and Azure OpenAI.
// Provider keys/settings managed via IAiProviderSettings.
// =============================================================================

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

// ── Provider Configuration ──

public enum AiProvider
{
    Anthropic,
    OpenAI,
    Google,
    AzureOpenAI
}

public sealed record AiProviderConfig(
    AiProvider Provider,
    string ApiKey,
    string ModelId,
    float Temperature,
    string? BaseUrl,        // For Azure OpenAI custom endpoints
    string? ApiVersion,     // Azure API version
    bool IsEnabled);

public sealed record AiSettingsResponse(
    AiProvider ActiveProvider,
    IReadOnlyList<AiProviderConfigView> Providers,
    AiGenerationDefaults Defaults);

public sealed record AiProviderConfigView(
    AiProvider Provider,
    string DisplayName,
    bool IsEnabled,
    bool HasApiKey,
    string ModelId,
    float Temperature,
    string? BaseUrl);

public sealed record AiGenerationDefaults(
    string DefaultLanguage,
    int DefaultBloomsLevel,
    string DefaultGrade,
    int QuestionsPerBatch,
    bool AutoRunQualityGate);

public sealed record UpdateAiSettingsRequest(
    AiProvider? ActiveProvider,
    string? ApiKey,
    string? ModelId,
    float? Temperature,
    string? BaseUrl,
    string? ApiVersion,
    // Generation defaults
    string? DefaultLanguage,
    int? DefaultBloomsLevel,
    string? DefaultGrade,
    int? QuestionsPerBatch,
    bool? AutoRunQualityGate);

// ── Generation Request/Response ──

public sealed record AiGenerateRequest(
    string Subject,
    string? Topic,
    string Grade,
    int BloomsLevel,
    float MinDifficulty,          // Range start (0=easy, 1=hard)
    float MaxDifficulty,          // Range end (0=easy, 1=hard)
    string Language,
    string? Context,              // Free-text, OCR output, or extracted content
    string? ImageBase64,          // Base64-encoded question image for vision models
    string? FileName,             // Original filename for document context
    string? StyleContext,         // Free-text style description
    string? StyleImageBase64,     // Base64-encoded style reference image
    string? StyleFileName,        // Original filename for style image
    int Count);                   // How many questions to generate

public sealed record AiGeneratedQuestion(
    string Stem,
    IReadOnlyList<AiGeneratedOption> Options,
    string? Topic,
    int BloomsLevel,
    float Difficulty,
    string Explanation);

public sealed record AiGeneratedOption(
    string Label,
    string Text,
    bool IsCorrect,
    string? DistractorRationale);

public sealed record AiGenerateResponse(
    bool Success,
    IReadOnlyList<AiGeneratedQuestion> Questions,
    string PromptUsed,
    string ModelUsed,
    float TemperatureUsed,
    string? RawOutput,
    string? Error);

// ── Service Interface ──

public interface IAiGenerationService
{
    Task<AiGenerateResponse> GenerateQuestionsAsync(AiGenerateRequest request);
    Task<AiSettingsResponse> GetSettingsAsync();
    Task<bool> UpdateSettingsAsync(UpdateAiSettingsRequest request, string userId);
    Task<bool> TestConnectionAsync(AiProvider provider);
}

// ── Implementation ──

public sealed class AiGenerationService : IAiGenerationService
{
    private readonly ILogger<AiGenerationService> _logger;

    // In-memory settings (production: persist to Marten document store)
    private AiProvider _activeProvider = AiProvider.Anthropic;
    private readonly Dictionary<AiProvider, AiProviderConfig> _providers;
    private AiGenerationDefaults _defaults;

    public AiGenerationService(ILogger<AiGenerationService> logger)
    {
        _logger = logger;

        _providers = new()
        {
            [AiProvider.Anthropic] = new(AiProvider.Anthropic, "", "claude-sonnet-4-6", 0.7f, null, null, true),
            [AiProvider.OpenAI] = new(AiProvider.OpenAI, "", "gpt-4o", 0.7f, null, null, false),
            [AiProvider.Google] = new(AiProvider.Google, "", "gemini-2.0-flash", 0.7f, null, null, false),
            [AiProvider.AzureOpenAI] = new(AiProvider.AzureOpenAI, "", "gpt-4o", 0.7f, null, "2024-12-01-preview", false),
        };

        _defaults = new("he", 3, "4 Units", 5, true);
    }

    public async Task<AiGenerateResponse> GenerateQuestionsAsync(AiGenerateRequest request)
    {
        var config = _providers[_activeProvider];
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                "", config.ModelId, config.Temperature, null,
                $"No API key configured for {_activeProvider}. Go to Settings > AI Providers to add one.");
        }

        var prompt = BuildPrompt(request, config);

        _logger.LogInformation(
            "Generating {Count} questions via {Provider}/{Model} for {Subject}/{Grade}",
            request.Count, _activeProvider, config.ModelId, request.Subject, request.Grade);

        try
        {
            // Dispatch to provider-specific implementation
            var (rawOutput, questions) = _activeProvider switch
            {
                AiProvider.Anthropic => await CallAnthropicAsync(config, prompt, request),
                AiProvider.OpenAI => await CallOpenAiAsync(config, prompt, request),
                AiProvider.Google => await CallGoogleAsync(config, prompt, request),
                AiProvider.AzureOpenAI => await CallAzureOpenAiAsync(config, prompt, request),
                _ => throw new NotSupportedException($"Provider {_activeProvider} not implemented")
            };

            return new AiGenerateResponse(true, questions, prompt, config.ModelId,
                config.Temperature, rawOutput, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation failed for {Provider}", _activeProvider);
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                prompt, config.ModelId, config.Temperature, null, ex.Message);
        }
    }

    public Task<AiSettingsResponse> GetSettingsAsync()
    {
        var views = _providers.Values.Select(p => new AiProviderConfigView(
            p.Provider,
            p.Provider switch
            {
                AiProvider.Anthropic => "Anthropic (Claude)",
                AiProvider.OpenAI => "OpenAI (GPT)",
                AiProvider.Google => "Google (Gemini)",
                AiProvider.AzureOpenAI => "Azure OpenAI",
                _ => p.Provider.ToString()
            },
            p.IsEnabled,
            !string.IsNullOrEmpty(p.ApiKey),
            p.ModelId,
            p.Temperature,
            p.BaseUrl)).ToList();

        return Task.FromResult(new AiSettingsResponse(_activeProvider, views, _defaults));
    }

    public Task<bool> UpdateSettingsAsync(UpdateAiSettingsRequest request, string userId)
    {
        if (request.ActiveProvider.HasValue)
            _activeProvider = request.ActiveProvider.Value;

        // Update the active provider's config
        var current = _providers[_activeProvider];
        _providers[_activeProvider] = current with
        {
            ApiKey = request.ApiKey ?? current.ApiKey,
            ModelId = request.ModelId ?? current.ModelId,
            Temperature = request.Temperature ?? current.Temperature,
            BaseUrl = request.BaseUrl ?? current.BaseUrl,
            ApiVersion = request.ApiVersion ?? current.ApiVersion,
            IsEnabled = true
        };

        // Update generation defaults
        _defaults = new(
            request.DefaultLanguage ?? _defaults.DefaultLanguage,
            request.DefaultBloomsLevel ?? _defaults.DefaultBloomsLevel,
            request.DefaultGrade ?? _defaults.DefaultGrade,
            request.QuestionsPerBatch ?? _defaults.QuestionsPerBatch,
            request.AutoRunQualityGate ?? _defaults.AutoRunQualityGate);

        _logger.LogInformation("AI settings updated by {UserId}: provider={Provider}, model={Model}",
            userId, _activeProvider, _providers[_activeProvider].ModelId);

        return Task.FromResult(true);
    }

    public Task<bool> TestConnectionAsync(AiProvider provider)
    {
        var config = _providers[provider];
        var hasKey = !string.IsNullOrEmpty(config.ApiKey);
        _logger.LogInformation("Testing {Provider} connection: hasKey={HasKey}", provider, hasKey);
        return Task.FromResult(hasKey);
    }

    // ── Prompt Builder ──

    private static string BuildPrompt(AiGenerateRequest req, AiProviderConfig config)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are an expert educational content creator for the Israeli Bagrut curriculum.");
        sb.AppendLine($"Generate {req.Count} multiple-choice question(s) with the following specifications:");
        sb.AppendLine($"- Subject: {req.Subject}");
        if (!string.IsNullOrEmpty(req.Topic)) sb.AppendLine($"- Topic: {req.Topic}");
        sb.AppendLine($"- Grade/Level: {req.Grade}");
        sb.AppendLine($"- Bloom's Taxonomy Level: {req.BloomsLevel} ({BloomLabel(req.BloomsLevel)})");

        // Difficulty range — distribute questions across the range
        if (Math.Abs(req.MinDifficulty - req.MaxDifficulty) < 0.01f)
        {
            sb.AppendLine($"- Target Difficulty: {req.MinDifficulty:F2} (0=easy, 1=hard)");
        }
        else
        {
            sb.AppendLine($"- Difficulty Range: {req.MinDifficulty:F2} to {req.MaxDifficulty:F2} (0=easy, 1=hard)");
            sb.AppendLine($"  Distribute the {req.Count} question(s) evenly across this difficulty range.");
        }

        sb.AppendLine($"- Language: {LangLabel(req.Language)}");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("- Each question must have exactly 4 options (A, B, C, D)");
        sb.AppendLine("- Exactly one correct answer per question");
        sb.AppendLine("- Each distractor must target a specific misconception (provide rationale)");
        sb.AppendLine("- Questions must align with Bagrut curriculum standards");
        sb.AppendLine("- Avoid cultural insensitivity for Israeli Hebrew/Arabic student populations");
        sb.AppendLine();

        // Question source material (photo OCR or text)
        if (!string.IsNullOrEmpty(req.Context))
        {
            sb.AppendLine("Context/Source material (use this as the basis for question generation):");
            sb.AppendLine(req.Context);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(req.ImageBase64))
        {
            sb.AppendLine("A question/source image has been attached. Use the content visible in the image as the basis for generating questions.");
            sb.AppendLine();
        }

        // Style reference
        if (!string.IsNullOrEmpty(req.StyleContext) || !string.IsNullOrEmpty(req.StyleImageBase64))
        {
            sb.AppendLine("STYLE REFERENCE — match the style, tone, and format of these questions:");
            if (!string.IsNullOrEmpty(req.StyleContext))
            {
                sb.AppendLine(req.StyleContext);
            }
            if (!string.IsNullOrEmpty(req.StyleImageBase64))
            {
                sb.AppendLine("A style reference image has been attached. Match the question format, phrasing style, and complexity pattern shown in that image.");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Respond in JSON array format:");
        sb.AppendLine("[{\"stem\": \"...\", \"options\": [{\"label\": \"A\", \"text\": \"...\", \"isCorrect\": true/false, \"distractorRationale\": \"...\"}], \"topic\": \"...\", \"bloomsLevel\": N, \"difficulty\": 0.X, \"explanation\": \"...\"}]");

        return sb.ToString();
    }

    private static string BloomLabel(int level) => level switch
    {
        1 => "Remember", 2 => "Understand", 3 => "Apply",
        4 => "Analyze", 5 => "Evaluate", 6 => "Create", _ => "Unknown"
    };

    private static string LangLabel(string lang) => lang switch
    {
        "he" => "Hebrew", "ar" => "Arabic", "en" => "English", _ => lang
    };

    // ── Provider-Specific Implementations ──
    // Each returns (rawOutput, parsedQuestions).
    // Real implementations use HttpClient to call provider APIs.
    // Currently returns mock data — replace with actual HTTP calls.

    private async Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallAnthropicAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        // TODO: Replace with actual Anthropic API call
        // POST https://api.anthropic.com/v1/messages
        // Headers: x-api-key, anthropic-version
        await Task.Delay(500); // Simulate latency
        return GenerateMockResponse(req);
    }

    private async Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallOpenAiAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        // TODO: Replace with actual OpenAI API call
        // POST https://api.openai.com/v1/chat/completions
        await Task.Delay(500);
        return GenerateMockResponse(req);
    }

    private async Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallGoogleAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        // TODO: Replace with actual Google Gemini API call
        // POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
        await Task.Delay(500);
        return GenerateMockResponse(req);
    }

    private async Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallAzureOpenAiAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        // TODO: Replace with actual Azure OpenAI API call
        // POST {baseUrl}/openai/deployments/{model}/chat/completions?api-version={version}
        await Task.Delay(500);
        return GenerateMockResponse(req);
    }

    private static (string, IReadOnlyList<AiGeneratedQuestion>) GenerateMockResponse(AiGenerateRequest req)
    {
        var questions = new List<AiGeneratedQuestion>();
        for (int i = 0; i < req.Count; i++)
        {
            // Distribute difficulty evenly across the range
            var difficulty = req.Count == 1
                ? (req.MinDifficulty + req.MaxDifficulty) / 2f
                : req.MinDifficulty + (req.MaxDifficulty - req.MinDifficulty) * i / (req.Count - 1f);

            var styleNote = !string.IsNullOrEmpty(req.StyleContext) || !string.IsNullOrEmpty(req.StyleImageBase64)
                ? " (styled)" : "";

            questions.Add(new AiGeneratedQuestion(
                Stem: $"[AI-Generated{styleNote}] Sample {req.Subject} question about {req.Topic ?? req.Subject} (#{i + 1}, difficulty {difficulty:F2})",
                Options: new[]
                {
                    new AiGeneratedOption("A", "Correct answer placeholder", true, null),
                    new AiGeneratedOption("B", "Distractor 1", false, "Common misconception"),
                    new AiGeneratedOption("C", "Distractor 2", false, "Calculation error"),
                    new AiGeneratedOption("D", "Distractor 3", false, "Conceptual confusion"),
                },
                Topic: req.Topic,
                BloomsLevel: req.BloomsLevel,
                Difficulty: difficulty,
                Explanation: "This question tests the student's ability to apply fundamental concepts."));
        }

        var rawJson = JsonSerializer.Serialize(questions, new JsonSerializerOptions { WriteIndented = true });
        return (rawJson, questions);
    }
}
