// =============================================================================
// Cena Platform — AI Question Generation Service
// Provider-agnostic abstraction for LLM-based question generation.
// Anthropic (Claude) is the only implemented provider. Others throw
// NotImplementedException per Task-00 spec.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
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

// ── Batch Generation Request/Response (CNT-002) ──

/// <summary>
/// Generate N questions that share the same subject/topic/grade/Bloom/difficulty parameters.
/// count is clamped to 1-20 per call.
/// </summary>
public sealed record BatchGenerateRequest(
    int Count,                     // 1-20 questions
    string Subject,
    string? Topic,
    string Grade,
    int BloomsLevel,
    float MinDifficulty,
    float MaxDifficulty,
    string Language);

public sealed record BatchGenerateResult(
    AiGeneratedQuestion Question,
    QualityGate.QualityGateResult QualityGate,
    bool PassedQualityGate);

public sealed record BatchGenerateResponse(
    bool Success,
    IReadOnlyList<BatchGenerateResult> Results,
    int TotalGenerated,
    int PassedQualityGate,
    int NeedsReview,
    int AutoRejected,
    string ModelUsed,
    string? Error);

// ── Template (OCR) Generation Request/Response (CNT-002) ──

/// <summary>
/// Generate questions that match the style and format of an exam paper captured via OCR.
/// The ocrText is used as both context and style reference.
/// </summary>
public sealed record TemplateGenerateRequest(
    string OcrText,               // OCR-extracted text from exam paper
    int Count,                    // 1-20 questions
    string Subject,
    string? Topic,
    string Grade,
    int BloomsLevel,
    float MinDifficulty,
    float MaxDifficulty,
    string Language);

public sealed record TemplateGenerateResponse(
    bool Success,
    IReadOnlyList<BatchGenerateResult> Results,
    int TotalGenerated,
    int PassedQualityGate,
    int NeedsReview,
    int AutoRejected,
    string ModelUsed,
    string? Error);

// ── Circuit Breaker (in-process, mirrors LlmCircuitBreakerActor thresholds) ──

public sealed class CircuitOpenException : Exception
{
    public CircuitOpenException(string message) : base(message) { }
}

// ── Service Interface ──

public interface IAiGenerationService
{
    Task<AiGenerateResponse> GenerateQuestionsAsync(AiGenerateRequest request);
    Task<AiSettingsResponse> GetSettingsAsync();
    Task<bool> UpdateSettingsAsync(UpdateAiSettingsRequest request, string userId);
    Task<bool> TestConnectionAsync(AiProvider provider);
    Task<BatchGenerateResponse> BatchGenerateAsync(BatchGenerateRequest request, QualityGate.IQualityGateService qualityGate);
    Task<TemplateGenerateResponse> GenerateFromTemplateAsync(TemplateGenerateRequest request, QualityGate.IQualityGateService qualityGate);
}

// ── Implementation ──

public sealed class AiGenerationService : IAiGenerationService
{
    private readonly ILogger<AiGenerationService> _logger;
    private readonly IConfiguration _configuration;

    // Observability — routing-config.yaml section 9
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _tokensTotal;
    private readonly Counter<double> _costUsd;

    // In-process circuit breaker state (mirrors LlmCircuitBreakerActor: Sonnet 3/90s)
    private int _failureCount;
    private DateTimeOffset _circuitOpenedAt;
    private bool _circuitOpen;
    private static readonly int MaxFailures = 3;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(90);
    private readonly object _cbLock = new();

    // Anthropic SDK client — lazily created when API key is set
    private AnthropicClient? _anthropicClient;
    private string? _lastApiKey;

    // In-memory settings (production: persist to Marten document store)
    private AiProvider _activeProvider = AiProvider.Anthropic;
    private readonly Dictionary<AiProvider, AiProviderConfig> _providers;
    private AiGenerationDefaults _defaults;

    // JSON options for parsing tool_use output
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Routing config constants (from routing-config.yaml, task: question_generation ~ video_script)
    private const string SonnetModelId = "claude-sonnet-4-6-20260215";
    private const float DefaultTemperature = 0.5f;
    private const int DefaultMaxTokens = 4096;
    // Cost per million tokens (routing-config.yaml section 1: claude_sonnet_4_6)
    private const double CostPerInputMTok = 3.00;
    private const double CostPerOutputMTok = 15.00;

    public AiGenerationService(
        ILogger<AiGenerationService> logger,
        IConfiguration configuration,
        IMeterFactory meterFactory)
    {
        _logger = logger;
        _configuration = configuration;

        var meter = meterFactory.Create("Cena.Admin.LlmMetrics", "1.0.0");
        _requestDuration = meter.CreateHistogram<double>(
            "llm_request_duration_ms",
            unit: "ms",
            description: "LLM request duration in milliseconds");
        _tokensTotal = meter.CreateCounter<long>(
            "llm_tokens_total",
            description: "Total LLM tokens consumed");
        _costUsd = meter.CreateCounter<double>(
            "llm_cost_usd",
            unit: "USD",
            description: "LLM cost in USD");

        _providers = new()
        {
            [AiProvider.Anthropic] = new(AiProvider.Anthropic, "", SonnetModelId,
                DefaultTemperature, null, null, true),
            [AiProvider.OpenAI] = new(AiProvider.OpenAI, "", "gpt-4o",
                0.7f, null, null, false),
            [AiProvider.Google] = new(AiProvider.Google, "", "gemini-2.0-flash",
                0.7f, null, null, false),
            [AiProvider.AzureOpenAI] = new(AiProvider.AzureOpenAI, "", "gpt-4o",
                0.7f, null, "2024-12-01-preview", false),
        };

        _defaults = new("he", 3, "4 Units", 5, true);
    }

    public async Task<AiGenerateResponse> GenerateQuestionsAsync(AiGenerateRequest request)
    {
        var config = _providers[_activeProvider];

        // Resolve API key: explicit config first, then IConfiguration
        var apiKey = !string.IsNullOrEmpty(config.ApiKey)
            ? config.ApiKey
            : _configuration["Anthropic:ApiKey"];

        if (_activeProvider == AiProvider.Anthropic && string.IsNullOrEmpty(apiKey))
        {
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                "", config.ModelId, config.Temperature, null,
                "No API key configured for Anthropic. Set Anthropic:ApiKey in configuration or go to Settings > AI Providers.");
        }

        if (_activeProvider != AiProvider.Anthropic && string.IsNullOrEmpty(config.ApiKey))
        {
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                "", config.ModelId, config.Temperature, null,
                $"No API key configured for {_activeProvider}. Go to Settings > AI Providers to add one.");
        }

        // For Anthropic, inject the resolved key into config for downstream use
        var effectiveConfig = _activeProvider == AiProvider.Anthropic
            ? config with { ApiKey = apiKey! }
            : config;

        var prompt = BuildPrompt(request, effectiveConfig);

        _logger.LogInformation(
            "Generating {Count} questions via {Provider}/{Model} for {Subject}/{Grade}",
            request.Count, _activeProvider, effectiveConfig.ModelId, request.Subject, request.Grade);

        try
        {
            var (rawOutput, questions) = _activeProvider switch
            {
                AiProvider.Anthropic => await CallAnthropicAsync(effectiveConfig, prompt, request),
                AiProvider.OpenAI => await CallOpenAiAsync(effectiveConfig, prompt, request),
                AiProvider.Google => await CallGoogleAsync(effectiveConfig, prompt, request),
                AiProvider.AzureOpenAI => await CallAzureOpenAiAsync(effectiveConfig, prompt, request),
                _ => throw new NotSupportedException($"Provider {_activeProvider} not implemented")
            };

            return new AiGenerateResponse(true, questions, prompt, effectiveConfig.ModelId,
                effectiveConfig.Temperature, rawOutput, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation failed for {Provider}", _activeProvider);
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                prompt, effectiveConfig.ModelId, effectiveConfig.Temperature, null, ex.Message);
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
            !string.IsNullOrEmpty(p.ApiKey) || !string.IsNullOrEmpty(_configuration["Anthropic:ApiKey"]),
            p.ModelId,
            p.Temperature,
            p.BaseUrl)).ToList();

        return Task.FromResult(new AiSettingsResponse(_activeProvider, views, _defaults));
    }

    public Task<bool> UpdateSettingsAsync(UpdateAiSettingsRequest request, string userId)
    {
        if (request.ActiveProvider.HasValue)
            _activeProvider = request.ActiveProvider.Value;

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
        var hasKey = !string.IsNullOrEmpty(config.ApiKey)
            || (provider == AiProvider.Anthropic && !string.IsNullOrEmpty(_configuration["Anthropic:ApiKey"]));
        _logger.LogInformation("Testing {Provider} connection: hasKey={HasKey}", provider, hasKey);
        return Task.FromResult(hasKey);
    }

    // ── Prompt Builder ──

    private static string BuildPrompt(AiGenerateRequest req, AiProviderConfig config)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Generate {req.Count} multiple-choice question(s) with the following specifications:");
        sb.AppendLine($"- Subject: {req.Subject}");
        if (!string.IsNullOrEmpty(req.Topic)) sb.AppendLine($"- Topic: {req.Topic}");
        sb.AppendLine($"- Grade/Level: {req.Grade}");
        sb.AppendLine($"- Bloom's Taxonomy Level: {req.BloomsLevel} ({BloomLabel(req.BloomsLevel)})");

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

        if (!string.IsNullOrEmpty(req.StyleContext) || !string.IsNullOrEmpty(req.StyleImageBase64))
        {
            sb.AppendLine("STYLE REFERENCE — match the style, tone, and format of these questions:");
            if (!string.IsNullOrEmpty(req.StyleContext))
                sb.AppendLine(req.StyleContext);
            if (!string.IsNullOrEmpty(req.StyleImageBase64))
                sb.AppendLine("A style reference image has been attached. Match the question format, phrasing style, and complexity pattern shown in that image.");
            sb.AppendLine();
        }

        sb.AppendLine("Use the generate_questions tool to return your response as structured JSON.");
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

    // ── Circuit Breaker (in-process, mirrors LlmCircuitBreakerActor thresholds) ──

    private void RequestCircuitPermission(string modelName)
    {
        lock (_cbLock)
        {
            if (!_circuitOpen) return;

            if (DateTimeOffset.UtcNow - _circuitOpenedAt >= OpenDuration)
            {
                _logger.LogInformation("Circuit breaker half-open for {Model}, allowing probe", modelName);
                _circuitOpen = false;
                _failureCount = 0;
                return;
            }

            var retryAfter = OpenDuration - (DateTimeOffset.UtcNow - _circuitOpenedAt);
            throw new CircuitOpenException(
                $"Circuit breaker OPEN for model {modelName}. Retry after {retryAfter.TotalSeconds:F0}s.");
        }
    }

    private void RecordSuccess(string modelName)
    {
        lock (_cbLock)
        {
            _failureCount = 0;
            _circuitOpen = false;
        }
    }

    private void RecordFailure(string modelName)
    {
        lock (_cbLock)
        {
            _failureCount++;
            _logger.LogWarning("LLM failure for {Model}. Count={Count}/{Max}",
                modelName, _failureCount, MaxFailures);

            if (_failureCount >= MaxFailures)
            {
                _circuitOpen = true;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "Circuit breaker OPENED for {Model}. Failures={Count}, OpenDuration={Duration}s",
                    modelName, _failureCount, OpenDuration.TotalSeconds);
            }
        }
    }

    // ── Anthropic SDK Client ──

    private AnthropicClient GetOrCreateClient(string apiKey)
    {
        if (_anthropicClient is not null && _lastApiKey == apiKey)
            return _anthropicClient;

        _anthropicClient = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            MaxRetries = 0, // Circuit breaker handles retries
        });
        _lastApiKey = apiKey;
        return _anthropicClient;
    }

    // ── Tool schema for structured output ──

    private static readonly InputSchema QuestionToolSchema = InputSchema.FromRawUnchecked(
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"type":"object","properties":{"questions":{"type":"array","items":{"type":"object","properties":{"stem":{"type":"string"},"options":{"type":"array","items":{"type":"object","properties":{"label":{"type":"string"},"text":{"type":"string"},"isCorrect":{"type":"boolean"},"distractorRationale":{"type":"string"}},"required":["label","text","isCorrect"]}},"topic":{"type":"string"},"bloomsLevel":{"type":"integer"},"difficulty":{"type":"number"},"explanation":{"type":"string"}},"required":["stem","options","bloomsLevel","difficulty","explanation"]}}},"required":["questions"]}""")!);

    // ── Provider Implementations ──

    private async Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallAnthropicAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        var modelName = config.ModelId;
        RequestCircuitPermission(modelName);

        var client = GetOrCreateClient(config.ApiKey);
        var sw = Stopwatch.StartNew();

        try
        {
            // System prompt with cache_control: { type: "ephemeral" } per routing-config section 6
            var systemBlocks = new List<TextBlockParam>
            {
                new TextBlockParam
                {
                    Text = "You are an expert educational content creator for the Israeli Bagrut curriculum. " +
                           "Generate high-quality multiple-choice questions that align with curriculum standards. " +
                           "Each question must have exactly 4 options with one correct answer. " +
                           "Each distractor must target a specific misconception. " +
                           "Avoid cultural insensitivity for Israeli Hebrew/Arabic student populations.",
                    CacheControl = new CacheControlEphemeral()
                }
            };

            var tool = new Tool
            {
                Name = "generate_questions",
                Description = "Generate structured educational questions for Bagrut exam preparation",
                InputSchema = QuestionToolSchema
            };

            var createParams = new MessageCreateParams
            {
                Model = SonnetModelId,
                MaxTokens = DefaultMaxTokens,
                Temperature = config.Temperature,
                System = systemBlocks,
                Messages = new List<MessageParam>
                {
                    new MessageParam { Role = "user", Content = prompt }
                },
                Tools = new List<ToolUnion> { tool },
                ToolChoice = new ToolChoiceTool { Name = "generate_questions" },
            };

            var response = await client.Messages.Create(createParams);
            sw.Stop();

            // Record metrics
            var inputTokens = response.Usage.InputTokens;
            var outputTokens = response.Usage.OutputTokens;
            EmitMetrics(modelName, "question_generation", sw.ElapsedMilliseconds,
                inputTokens, outputTokens);

            // Extract tool_use block
            foreach (var block in response.Content)
            {
                if (block.TryPickToolUse(out var toolUse) && toolUse.Name == "generate_questions")
                {
                    var rawJson = JsonSerializer.Serialize(toolUse.Input, JsonOpts);
                    var questions = ParseToolUseQuestions(toolUse.Input);

                    RecordSuccess(modelName);
                    return (rawJson, questions);
                }
            }

            // Fallback: try parsing text content as JSON
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var textBlock))
                {
                    var questions = ParseJsonQuestions(textBlock.Text);
                    RecordSuccess(modelName);
                    return (textBlock.Text, questions);
                }
            }

            throw new InvalidOperationException("Anthropic response contained no tool_use or text blocks");
        }
        catch (CircuitOpenException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure(modelName);
            _logger.LogError(ex, "Anthropic API call failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallOpenAiAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        throw new NotImplementedException("Provider not yet implemented — use Anthropic");
    }

    private Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallGoogleAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        throw new NotImplementedException("Provider not yet implemented — use Anthropic");
    }

    private Task<(string, IReadOnlyList<AiGeneratedQuestion>)> CallAzureOpenAiAsync(
        AiProviderConfig config, string prompt, AiGenerateRequest req)
    {
        throw new NotImplementedException("Provider not yet implemented — use Anthropic");
    }

    // ── Response Parsing ──

    private static IReadOnlyList<AiGeneratedQuestion> ParseToolUseQuestions(
        IReadOnlyDictionary<string, JsonElement> toolInput)
    {
        if (!toolInput.TryGetValue("questions", out var questionsElement))
            throw new InvalidOperationException("Tool response missing 'questions' property");

        return ParseQuestionsFromElement(questionsElement);
    }

    private static IReadOnlyList<AiGeneratedQuestion> ParseJsonQuestions(string rawJson)
    {
        var trimmed = rawJson.Trim();

        // Handle wrapped-in-object format: { "questions": [...] }
        if (trimmed.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("questions", out var questionsEl))
                return ParseQuestionsFromElement(questionsEl);
        }

        // Handle raw array format: [...]
        if (trimmed.StartsWith('['))
        {
            using var doc = JsonDocument.Parse(trimmed);
            return ParseQuestionsFromElement(doc.RootElement);
        }

        throw new InvalidOperationException("Could not parse LLM response as question JSON");
    }

    private static IReadOnlyList<AiGeneratedQuestion> ParseQuestionsFromElement(JsonElement element)
    {
        var questions = new List<AiGeneratedQuestion>();

        foreach (var qEl in element.EnumerateArray())
        {
            var stem = qEl.GetProperty("stem").GetString() ?? "";
            var explanation = qEl.TryGetProperty("explanation", out var explEl)
                ? explEl.GetString() ?? "" : "";
            var topic = qEl.TryGetProperty("topic", out var topicEl)
                ? topicEl.GetString() : null;
            var bloomsLevel = qEl.TryGetProperty("bloomsLevel", out var blEl)
                ? blEl.GetInt32() : 3;
            var difficulty = qEl.TryGetProperty("difficulty", out var diffEl)
                ? (float)diffEl.GetDouble() : 0.5f;

            var options = new List<AiGeneratedOption>();
            if (qEl.TryGetProperty("options", out var optsEl))
            {
                foreach (var optEl in optsEl.EnumerateArray())
                {
                    var label = optEl.TryGetProperty("label", out var lblEl)
                        ? lblEl.GetString() ?? "" : "";
                    var text = optEl.TryGetProperty("text", out var txtEl)
                        ? txtEl.GetString() ?? "" : "";
                    var isCorrect = optEl.TryGetProperty("isCorrect", out var corEl)
                        && corEl.GetBoolean();
                    var rationale = optEl.TryGetProperty("distractorRationale", out var ratEl)
                        ? ratEl.GetString() : null;

                    options.Add(new AiGeneratedOption(label, text, isCorrect, rationale));
                }
            }

            questions.Add(new AiGeneratedQuestion(stem, options, topic, bloomsLevel, difficulty, explanation));
        }

        return questions;
    }

    // ── Observability ──

    private void EmitMetrics(string model, string taskType, long durationMs,
        long inputTokens, long outputTokens)
    {
        var modelTag = new KeyValuePair<string, object?>("model_id", model);
        var taskTag = new KeyValuePair<string, object?>("task_type", taskType);

        _requestDuration.Record(durationMs, modelTag, taskTag,
            new KeyValuePair<string, object?>("status", "success"));

        _tokensTotal.Add(inputTokens, modelTag, taskTag,
            new KeyValuePair<string, object?>("direction", "input"));
        _tokensTotal.Add(outputTokens, modelTag, taskTag,
            new KeyValuePair<string, object?>("direction", "output"));

        var cost = (inputTokens * CostPerInputMTok + outputTokens * CostPerOutputMTok) / 1_000_000.0;
        _costUsd.Add(cost, modelTag, taskTag);

        _logger.LogInformation(
            "LLM call completed: model={Model}, task={Task}, duration={DurationMs}ms, " +
            "input_tokens={InputTokens}, output_tokens={OutputTokens}, cost_usd={Cost:F6}",
            model, taskType, durationMs, inputTokens, outputTokens, cost);
    }

    // ── Batch Generation (CNT-002) ──

    public async Task<BatchGenerateResponse> BatchGenerateAsync(
        BatchGenerateRequest req, QualityGate.IQualityGateService qualityGate)
    {
        var count = Math.Clamp(req.Count, 1, 20);
        var min   = Math.Clamp(req.MinDifficulty, 0f, 1f);
        var max   = Math.Clamp(req.MaxDifficulty, 0f, 1f);
        if (max < min) (min, max) = (max, min);

        var generateReq = new AiGenerateRequest(
            Subject:          req.Subject,
            Topic:            req.Topic,
            Grade:            req.Grade,
            BloomsLevel:      req.BloomsLevel,
            MinDifficulty:    min,
            MaxDifficulty:    max,
            Language:         req.Language,
            Context:          null,
            ImageBase64:      null,
            FileName:         null,
            StyleContext:     null,
            StyleImageBase64: null,
            StyleFileName:    null,
            Count:            count);

        var generateResponse = await GenerateQuestionsAsync(generateReq);

        if (!generateResponse.Success)
        {
            return new BatchGenerateResponse(
                Success: false,
                Results: Array.Empty<BatchGenerateResult>(),
                TotalGenerated: 0,
                PassedQualityGate: 0,
                NeedsReview: 0,
                AutoRejected: 0,
                ModelUsed: generateResponse.ModelUsed,
                Error: generateResponse.Error);
        }

        var results = new List<BatchGenerateResult>();

        foreach (var question in generateResponse.Questions)
        {
            var questionId = Guid.NewGuid().ToString();
            var correctIndex = question.Options
                .Select((o, i) => (o, i))
                .FirstOrDefault(x => x.o.IsCorrect).i;

            var gateInput = new QualityGate.QualityGateInput(
                QuestionId:       questionId,
                Stem:             question.Stem,
                Options:          question.Options.Select(o =>
                    new QualityGate.QualityGateOption(o.Label, o.Text, o.IsCorrect, o.DistractorRationale))
                    .ToList(),
                CorrectOptionIndex: correctIndex,
                Subject:          req.Subject,
                Language:         req.Language,
                ClaimedBloomLevel: question.BloomsLevel,
                ClaimedDifficulty: question.Difficulty,
                Grade:            req.Grade,
                ConceptIds:       null);

            var gateResult = await qualityGate.EvaluateAsync(gateInput);
            var passed = gateResult.Decision != QualityGate.GateDecision.AutoRejected;

            results.Add(new BatchGenerateResult(question, gateResult, passed));
        }

        return new BatchGenerateResponse(
            Success:           true,
            Results:           results,
            TotalGenerated:    results.Count,
            PassedQualityGate: results.Count(r => r.QualityGate.Decision == QualityGate.GateDecision.AutoApproved),
            NeedsReview:       results.Count(r => r.QualityGate.Decision == QualityGate.GateDecision.NeedsReview),
            AutoRejected:      results.Count(r => r.QualityGate.Decision == QualityGate.GateDecision.AutoRejected),
            ModelUsed:         generateResponse.ModelUsed,
            Error:             null);
    }

    // ── Template (OCR) Generation (CNT-002) ──

    public async Task<TemplateGenerateResponse> GenerateFromTemplateAsync(
        TemplateGenerateRequest req, QualityGate.IQualityGateService qualityGate)
    {
        if (string.IsNullOrWhiteSpace(req.OcrText))
        {
            return new TemplateGenerateResponse(
                Success: false,
                Results: Array.Empty<BatchGenerateResult>(),
                TotalGenerated: 0,
                PassedQualityGate: 0,
                NeedsReview: 0,
                AutoRejected: 0,
                ModelUsed: _providers[_activeProvider].ModelId,
                Error: "OcrText is required for template-based generation.");
        }

        var count = Math.Clamp(req.Count, 1, 20);
        var min   = Math.Clamp(req.MinDifficulty, 0f, 1f);
        var max   = Math.Clamp(req.MaxDifficulty, 0f, 1f);
        if (max < min) (min, max) = (max, min);

        // The OCR text serves as both context (source material) and style reference
        var generateReq = new AiGenerateRequest(
            Subject:          req.Subject,
            Topic:            req.Topic,
            Grade:            req.Grade,
            BloomsLevel:      req.BloomsLevel,
            MinDifficulty:    min,
            MaxDifficulty:    max,
            Language:         req.Language,
            Context:          req.OcrText,
            ImageBase64:      null,
            FileName:         null,
            StyleContext:     $"Match the style, difficulty, and format of the following exam paper content:\n{req.OcrText}",
            StyleImageBase64: null,
            StyleFileName:    null,
            Count:            count);

        var generateResponse = await GenerateQuestionsAsync(generateReq);

        if (!generateResponse.Success)
        {
            return new TemplateGenerateResponse(
                Success: false,
                Results: Array.Empty<BatchGenerateResult>(),
                TotalGenerated: 0,
                PassedQualityGate: 0,
                NeedsReview: 0,
                AutoRejected: 0,
                ModelUsed: generateResponse.ModelUsed,
                Error: generateResponse.Error);
        }

        var results = new List<BatchGenerateResult>();

        foreach (var question in generateResponse.Questions)
        {
            var questionId = Guid.NewGuid().ToString();
            var correctIndex = question.Options
                .Select((o, i) => (o, i))
                .FirstOrDefault(x => x.o.IsCorrect).i;

            var gateInput = new QualityGate.QualityGateInput(
                QuestionId:       questionId,
                Stem:             question.Stem,
                Options:          question.Options.Select(o =>
                    new QualityGate.QualityGateOption(o.Label, o.Text, o.IsCorrect, o.DistractorRationale))
                    .ToList(),
                CorrectOptionIndex: correctIndex,
                Subject:          req.Subject,
                Language:         req.Language,
                ClaimedBloomLevel: question.BloomsLevel,
                ClaimedDifficulty: question.Difficulty,
                Grade:            req.Grade,
                ConceptIds:       null);

            var gateResult = await qualityGate.EvaluateAsync(gateInput);
            var passed = gateResult.Decision != QualityGate.GateDecision.AutoRejected;

            results.Add(new BatchGenerateResult(question, gateResult, passed));
        }

        return new TemplateGenerateResponse(
            Success:           true,
            Results:           results,
            TotalGenerated:    results.Count,
            PassedQualityGate: results.Count(r => r.QualityGate.Decision == QualityGate.GateDecision.AutoApproved),
            NeedsReview:       results.Count(r => r.QualityGate.Decision == QualityGate.GateDecision.NeedsReview),
            AutoRejected:      results.Count(r => r.QualityGate.Decision == QualityGate.GateDecision.AutoRejected),
            ModelUsed:         generateResponse.ModelUsed,
            Error:             null);
    }
}
