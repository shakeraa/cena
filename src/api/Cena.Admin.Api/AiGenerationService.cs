// =============================================================================
// Cena Platform — AI Question Generation Service
// LLM-based question generation service. Anthropic (Claude) is the only
// supported provider — secondary providers (OpenAI, Google, AzureOpenAI) were
// removed in FIND-arch-005 because they were stubs that threw
// NotImplementedException at runtime.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Cena.Actors.Cas;
using Cena.Admin.Api.AiSettings;
using Cena.Admin.Api.QualityGate;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api;

// ── Provider Configuration ──

/// <summary>
/// Supported LLM providers for question generation.
/// Only Anthropic is implemented; additional providers must be added here
/// alongside a real <c>CallXxxAsync</c> implementation (no stubs).
/// Serialized as a string ("Anthropic") so the admin SPA can key UI state
/// (per-provider api-key inputs, active-provider chip) by name; numeric
/// serialization caused apiKeys["Anthropic"] vs apiKeys[0] to mismatch and
/// silently dropped the API key on save.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiProvider
{
    Anthropic
}

/// <summary>
/// Request body for POST /api/admin/ai/test-connection.
///
/// Wrapping the enum in an object (vs. [FromBody] AiProvider directly) is
/// load-bearing: ofetch does NOT auto-encode raw-string bodies as JSON —
/// it sends Content-Type: text/plain, which the minimal-API binder rejected
/// with 415 before the probe ever ran (the SPA then surfaced a bare
/// "Failed" badge with no category).
///
/// <see cref="ApiKey"/> + <see cref="ModelId"/> are optional overrides that
/// let the SPA test the value currently typed in the form BEFORE clicking
/// Save Settings — so an operator pasting a fresh key + Test Connection
/// gets a verdict on the typed key, not on the previously persisted (and
/// possibly stale) cipher. Pre-existing behaviour is preserved when both
/// are null/blank: the service decrypts the persisted cipher and uses the
/// persisted modelId as before. Override values are NEVER persisted —
/// they live only in the request scope.
/// </summary>
public sealed record TestConnectionRequest(
    AiProvider Provider,
    string? ApiKey = null,
    string? ModelId = null);

public sealed record AiProviderConfig(
    AiProvider Provider,
    string ApiKey,
    string ModelId,
    float Temperature,
    string? BaseUrl,        // Optional custom base URL (reserved for future providers)
    string? ApiVersion,     // Optional API version (reserved for future providers)
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
///
/// SourceContext (optional, ADR-0059 §15.5): when set, the LLM receives the
/// content as creative starting point with explicit "do not copy literally"
/// guardrails. Used by the Bagrut variant-generation flow to produce
/// competency-equivalent variants of past Ministry questions. Null
/// produces today's metadata-only behaviour.
/// </summary>
public sealed record BatchGenerateRequest(
    int Count,                     // 1-20 questions
    string Subject,
    string? Topic,
    string Grade,
    int BloomsLevel,
    float MinDifficulty,
    float MaxDifficulty,
    string Language,
    string? SourceContext = null,
    string? SourceLatex = null);

public sealed record BatchGenerateResult(
    AiGeneratedQuestion Question,
    QualityGateResult QualityGate,
    bool PassedQualityGate,
    // RDY-034 / ADR-0002: per-candidate CAS gate outcome. Null means the gate
    // was not run (Off mode or non-math content path early-exit).
    string? CasOutcome = null,
    string? CasEngine = null,
    string? CasFailureReason = null);

public sealed record CasDropReason(
    string QuestionStem,
    string Engine,
    string Reason,
    double LatencyMs);

public sealed record BatchGenerateResponse(
    bool Success,
    IReadOnlyList<BatchGenerateResult> Results,
    int TotalGenerated,
    int PassedQualityGate,
    int NeedsReview,
    int AutoRejected,
    string ModelUsed,
    string? Error,
    // RDY-034: how many candidates were dropped because the CAS gate
    // contradicted the authored answer (Enforce mode only).
    int DroppedForCasFailure = 0,
    IReadOnlyList<CasDropReason>? CasDropReasons = null,
    // PRR-322f-audit / 2026-04-30: thread provenance from the underlying
    // GenerateQuestionsAsync response so persistence callers
    // (GenerateVariantsJobStrategy + AiIsomorphGenerator) can populate
    // QuestionDocument's AiGenerationState fields with REAL values
    // instead of null. Was discarded entirely before this audit;
    // variants shipped with `ModelTemperature: null` and a synthetic
    // breadcrumb in the PromptText field. Defaults preserve back-compat
    // for any third-party caller; new callers should pass them through.
    string? PromptUsed = null,
    float TemperatureUsed = 0f,
    string? RawOutput = null);

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
    string? Error,
    int DroppedForCasFailure = 0,
    IReadOnlyList<CasDropReason>? CasDropReasons = null);

// ── ADR-0062 Phase 1.5 — OCR cleanup pass ──
// Extracted to Cena.Admin.Api.Ingestion.IOcrTextEnhancer / OcrTextEnhancer
// in 2026-05-03 to keep AiGenerationService's surface area focused on
// question generation + settings + connection probing. The DTOs and
// interface live in Ingestion/OcrTextEnhancer.cs; the endpoint at
// POST /api/admin/ingestion/items/{id}/enhance-text injects
// IOcrTextEnhancer directly.

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
    /// <summary>
    /// Real Anthropic ping. By default uses the persisted (decrypted) API key
    /// and configured model. Optional <paramref name="apiKeyOverride"/> /
    /// <paramref name="modelIdOverride"/> let the SPA test the value typed
    /// in the form before persisting — overrides are scoped to the request
    /// and never written back to Marten. Returns a structured
    /// ConnectionTestResult with a categorized failure reason on the unhappy
    /// path. Never throws.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(
        AiProvider provider,
        string? apiKeyOverride = null,
        string? modelIdOverride = null,
        CancellationToken ct = default);
    Task<BatchGenerateResponse> BatchGenerateAsync(BatchGenerateRequest request, QualityGateServices.IQualityGateService qualityGate);
    Task<TemplateGenerateResponse> GenerateFromTemplateAsync(TemplateGenerateRequest request, QualityGateServices.IQualityGateService qualityGate);
}

// ── Implementation ──

// ADR-0045: Tool-use structured question-batch generation via Sonnet (4096
// tokens, Bagrut-curriculum-aligned prompts with ephemeral cache_control on
// the system block). New routing row: contracts/llm/routing-config.yaml
// §task_routing.question_generation. Tier 3.
// prr-046: finops cost-center "question-generation". Legacy per-service
// llm_cost_usd counter is preserved for backward compat; the canonical
// cross-feature dashboard uses cena_llm_call_cost_usd_total via ILlmCostMetric.
// ADR-0047: admin tool — prompt is composed from Bagrut curriculum references
// and admin-authored topic briefs at question-authoring time. No student
// profile fields or student free-text reach this seam; it runs strictly
// upstream of any student session.
[TaskRouting("tier3", "question_generation")]
[FeatureTag("question-generation")]
[PiiPreScrubbed("Admin tool — prompt composed from Bagrut curriculum references and admin-authored topic briefs at authoring time. No student profile fields or student free-text enter this seam.")]
public sealed class AiGenerationService : IAiGenerationService
{
    private readonly ILogger<AiGenerationService> _logger;
    private readonly IConfiguration _configuration;
    // RDY-034 / ADR-0002: AiGenerationService is registered as a Singleton,
    // but ICasVerificationGate is Scoped (it depends on Marten IDocumentStore).
    // Use IServiceScopeFactory to create a scope per generation request so the
    // CAS gate can be resolved correctly.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICasGateModeProvider _casGateMode;

    // Observability — routing-config.yaml section 9
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _tokensTotal;
    private readonly Counter<double> _costUsd;
    // prr-046: canonical per-feature cost counter (cena_llm_call_cost_usd_total).
    private readonly ILlmCostMetric _featureCost;
    // prr-143: trace-id stamping site for question-generation LLM calls.
    private readonly IActivityPropagator? _activityPropagator;

    // In-process circuit breaker state (mirrors LlmCircuitBreakerActor: Sonnet 3/90s)
    private int _failureCount;
    private DateTimeOffset _circuitOpenedAt;
    private bool _circuitOpen;
    private static readonly int MaxFailures = 3;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(90);
    private readonly object _cbLock = new();

    // Anthropic SDK client — lazily created when API key is set. The client
    // is keyed by the plaintext API key; cache hits when the persisted key
    // hasn't changed since the last call.
    private AnthropicClient? _anthropicClient;
    private string? _lastApiKey;

    // ── Persistence + cipher (production: Marten-backed AiSettingsDocument) ──
    private readonly IDocumentStore _documentStore;
    private readonly IApiKeyCipher _cipher;
    private readonly IAnthropicConnectionProbe _probe;

    // 5-second hot cache of the AiSettingsDocument so the per-request load
    // doesn't hit Marten on every Generate call. Invalidated on Update.
    private AiSettingsDocument? _cachedDoc;
    private DateTimeOffset _cachedAt;
    private static readonly TimeSpan DocCacheTtl = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _docCacheLock = new(1, 1);

    // JSON options for parsing tool_use output
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Routing config constants (from routing-config.yaml, task: question_generation ~ video_script)
    private const string SonnetModelId = "claude-sonnet-4-6";
    private const float DefaultTemperature = 0.5f;
    private const int DefaultMaxTokens = 4096;
    // Cost per million tokens (routing-config.yaml section 1: claude_sonnet_4_6)
    private const double CostPerInputMTok = 3.00;
    private const double CostPerOutputMTok = 15.00;

    public AiGenerationService(
        ILogger<AiGenerationService> logger,
        IConfiguration configuration,
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        ICasGateModeProvider casGateMode,
        ILlmCostMetric featureCost,
        IDocumentStore documentStore,
        IApiKeyCipher cipher,
        IAnthropicConnectionProbe probe,
        IActivityPropagator? activityPropagator = null)
    {
        _logger = logger;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _casGateMode = casGateMode;
        _featureCost = featureCost;
        _documentStore = documentStore;
        _cipher = cipher;
        _probe = probe;
        _activityPropagator = activityPropagator;

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
    }

    // ── Persistence helpers ───────────────────────────────────────────────

    /// <summary>
    /// Load the singleton settings doc, hitting Marten at most once per
    /// DocCacheTtl. Returns a fresh defaulted document when none exists yet
    /// (first run) — never returns null.
    /// </summary>
    private async Task<AiSettingsDocument> LoadDocAsync(CancellationToken ct = default)
    {
        await _docCacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedDoc is not null && DateTimeOffset.UtcNow - _cachedAt < DocCacheTtl)
                return _cachedDoc;

            AiSettingsDocument? doc = null;
            try
            {
                await using var session = _documentStore.QuerySession();
                doc = await session.LoadAsync<AiSettingsDocument>(AiSettingsDocument.SingletonId, ct).ConfigureAwait(false);
            }
            catch (Marten.Exceptions.MartenCommandException ex)
                when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
            {
                // The mt_doc_aisettingsdocument table doesn't exist yet —
                // happens on the very first GET before any save has run, or
                // in any environment where the migrator hasn't applied the
                // schema. Marten's CreateOrUpdate auto-create only fires on
                // writes, not reads. Treat as "no settings persisted" and
                // let the next save (which uses LightweightSession) trigger
                // schema creation. Logged as a one-time INFO so the
                // operator sees the cold-start.
                _logger.LogInformation(
                    "AiSettingsDocument table not yet created — returning defaults until first save");
                doc = null;
            }

            _cachedDoc = doc ?? new AiSettingsDocument();
            _cachedAt = DateTimeOffset.UtcNow;
            return _cachedDoc;
        }
        finally
        {
            _docCacheLock.Release();
        }
    }

    /// <summary>Persist the doc and refresh the hot cache atomically.</summary>
    private async Task SaveDocAsync(AiSettingsDocument doc, CancellationToken ct = default)
    {
        doc.Id = AiSettingsDocument.SingletonId;
        doc.UpdatedAt = DateTimeOffset.UtcNow;

        await using var session = _documentStore.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);

        await _docCacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cachedDoc = doc;
            _cachedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _docCacheLock.Release();
        }
    }

    /// <summary>
    /// Resolve the active API key: persisted (decrypt-from-cipher) first,
    /// IConfiguration <c>Anthropic:ApiKey</c> as fallback. Returns null when
    /// neither source has a key. Decryption failures (tampered cipher,
    /// missing master key) log an error and treat the key as unset.
    /// </summary>
    private string? ResolveApiKey(AiSettingsDocument doc)
    {
        if (!string.IsNullOrEmpty(doc.AnthropicApiKeyCipher))
        {
            if (_cipher.TryDecryptFromWire(doc.AnthropicApiKeyCipher, out var plaintext))
                return plaintext;

            _logger.LogError(
                "[SIEM] Failed to decrypt persisted Anthropic API key — master key may have rotated, " +
                "or the cipher blob is corrupt. Falling back to IConfiguration.");
        }

        var fromConfig = _configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }

    /// <summary>
    /// Project a doc into the public AiProviderConfig view used by the
    /// internal call sites. The returned ApiKey is plaintext (resolved via
    /// <see cref="ResolveApiKey"/>); never store this value back.
    /// </summary>
    private AiProviderConfig ProjectAnthropicConfig(AiSettingsDocument doc, string? resolvedApiKey)
    {
        return new AiProviderConfig(
            AiProvider.Anthropic,
            resolvedApiKey ?? "",
            string.IsNullOrWhiteSpace(doc.AnthropicModelId) ? SonnetModelId : doc.AnthropicModelId,
            doc.AnthropicTemperature,
            doc.AnthropicBaseUrl,
            doc.AnthropicApiVersion,
            doc.AnthropicEnabled);
    }

    public async Task<AiGenerateResponse> GenerateQuestionsAsync(AiGenerateRequest request)
    {
        var doc = await LoadDocAsync().ConfigureAwait(false);
        var apiKey = ResolveApiKey(doc);
        var effectiveConfig = ProjectAnthropicConfig(doc, apiKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                "", effectiveConfig.ModelId, effectiveConfig.Temperature, null,
                "No API key configured for Anthropic. Set Anthropic:ApiKey in configuration or go to Settings > AI Providers.");
        }

        var prompt = AiPromptBuilder.BuildPrompt(request, effectiveConfig);

        _logger.LogInformation(
            "Generating {Count} questions via Anthropic/{Model} for {Subject}/{Grade}",
            request.Count, effectiveConfig.ModelId, request.Subject, request.Grade);

        try
        {
            var (rawOutput, questions) = await CallAnthropicAsync(effectiveConfig, prompt, request);

            return new AiGenerateResponse(true, questions, prompt, effectiveConfig.ModelId,
                effectiveConfig.Temperature, rawOutput, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation failed for Anthropic");
            return new AiGenerateResponse(false, Array.Empty<AiGeneratedQuestion>(),
                prompt, effectiveConfig.ModelId, effectiveConfig.Temperature, null, ex.Message);
        }
    }

    public async Task<AiSettingsResponse> GetSettingsAsync()
    {
        var doc = await LoadDocAsync().ConfigureAwait(false);

        var hasApiKey = !string.IsNullOrEmpty(doc.AnthropicApiKeyCipher)
            || !string.IsNullOrEmpty(_configuration["Anthropic:ApiKey"]);

        var view = new AiProviderConfigView(
            AiProvider.Anthropic,
            "Anthropic (Claude)",
            doc.AnthropicEnabled,
            hasApiKey,
            string.IsNullOrWhiteSpace(doc.AnthropicModelId) ? SonnetModelId : doc.AnthropicModelId,
            doc.AnthropicTemperature,
            doc.AnthropicBaseUrl);

        var defaults = new AiGenerationDefaults(
            doc.DefaultLanguage,
            doc.DefaultBloomsLevel,
            doc.DefaultGrade,
            doc.QuestionsPerBatch,
            doc.AutoRunQualityGate);

        // ActiveProvider in the doc is stored as a string for forward-compat;
        // parse defensively and fall back to Anthropic on unknown values.
        var active = Enum.TryParse<AiProvider>(doc.ActiveProvider, ignoreCase: true, out var parsed)
            ? parsed
            : AiProvider.Anthropic;

        return new AiSettingsResponse(active, new[] { view }, defaults);
    }

    public async Task<bool> UpdateSettingsAsync(UpdateAiSettingsRequest request, string userId)
    {
        var doc = await LoadDocAsync().ConfigureAwait(false);
        // Mutate a copy — the cached doc must not change until the save
        // succeeds, otherwise a failed write leaves stale state in memory.
        var next = new AiSettingsDocument
        {
            Id = AiSettingsDocument.SingletonId,
            ActiveProvider = request.ActiveProvider?.ToString() ?? doc.ActiveProvider,
            AnthropicApiKeyCipher = doc.AnthropicApiKeyCipher,
            AnthropicModelId = request.ModelId ?? doc.AnthropicModelId,
            AnthropicTemperature = request.Temperature ?? doc.AnthropicTemperature,
            AnthropicBaseUrl = request.BaseUrl ?? doc.AnthropicBaseUrl,
            AnthropicApiVersion = request.ApiVersion ?? doc.AnthropicApiVersion,
            AnthropicEnabled = doc.AnthropicEnabled,
            DefaultLanguage = request.DefaultLanguage ?? doc.DefaultLanguage,
            DefaultBloomsLevel = request.DefaultBloomsLevel ?? doc.DefaultBloomsLevel,
            DefaultGrade = request.DefaultGrade ?? doc.DefaultGrade,
            QuestionsPerBatch = request.QuestionsPerBatch ?? doc.QuestionsPerBatch,
            AutoRunQualityGate = request.AutoRunQualityGate ?? doc.AutoRunQualityGate,
            UpdatedBy = userId,
        };

        // Encrypt the new key, if one was supplied. An empty/whitespace value
        // means "leave existing key alone"; the SPA passes apiKey: undefined
        // when the user is editing other fields and not the key.
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            next.AnthropicApiKeyCipher = _cipher.EncryptToWire(request.ApiKey);
        }

        await SaveDocAsync(next).ConfigureAwait(false);

        _logger.LogInformation(
            "AI settings updated by {UserId}: provider={Provider}, model={Model}, keyChanged={KeyChanged}",
            userId, next.ActiveProvider, next.AnthropicModelId,
            !string.IsNullOrWhiteSpace(request.ApiKey));

        return true;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        AiProvider provider,
        string? apiKeyOverride = null,
        string? modelIdOverride = null,
        CancellationToken ct = default)
    {
        if (provider != AiProvider.Anthropic)
        {
            _logger.LogWarning("TestConnection requested for unsupported provider {Provider}", provider);
            return ConnectionTestResult.Fail(
                $"Provider '{provider}' not supported", "UNSUPPORTED_PROVIDER");
        }

        var doc = await LoadDocAsync(ct).ConfigureAwait(false);

        // Override > persisted cipher > IConfiguration. Lets the SPA test a
        // typed-but-not-yet-saved key without persisting it first.
        var apiKey = !string.IsNullOrWhiteSpace(apiKeyOverride)
            ? apiKeyOverride
            : ResolveApiKey(doc);

        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Fail("No API key configured", "CONFIG_MISSING_KEY");
        }

        // Override > persisted modelId > default Sonnet pin.
        var modelId = !string.IsNullOrWhiteSpace(modelIdOverride)
            ? modelIdOverride
            : (string.IsNullOrWhiteSpace(doc.AnthropicModelId) ? SonnetModelId : doc.AnthropicModelId);

        _logger.LogInformation(
            "Testing Anthropic connection (model={Model}, baseUrl={BaseUrl}, source={Source})",
            modelId,
            doc.AnthropicBaseUrl ?? "(default)",
            !string.IsNullOrWhiteSpace(apiKeyOverride) ? "request-override" : "persisted-cipher");

        return await _probe.ProbeAsync(apiKey, modelId, doc.AnthropicBaseUrl, ct).ConfigureAwait(false);
    }

    // ── Prompt Builder ──

    // PRR-304: prompt-template assembly extracted to AiPromptBuilder.cs
    // (BuildPrompt + BloomLabel + LangLabel). Behaviour-preserving extract;
    // see AiPromptBuilder.BuildPrompt for the prompt-string contract.

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
                // Use the configured model, not the hardcoded fallback. The
                // admin can pick any Anthropic model via the SPA dropdown;
                // pinning SonnetModelId here silently ignored that choice.
                Model = modelName,
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

            // prr-143: stamp trace_id on the outbound LLM attempt so the
            // question-generation path is stitchable in the observability
            // backend with the quality-gate LLM call that validates its output.
            var traceId = _activityPropagator?.GetTraceId();
            using var activity = _activityPropagator?.StartLlmActivity("question_generation");
            activity?.SetTag("trace_id", traceId);
            activity?.SetTag("task", "question_generation");
            activity?.SetTag("tier", "tier3");
            activity?.SetTag("model_id", modelName);

            var response = await client.Messages.Create(createParams);
            sw.Stop();

            // Record metrics
            var inputTokens = response.Usage.InputTokens;
            var outputTokens = response.Usage.OutputTokens;
            EmitMetrics(modelName, "question_generation", sw.ElapsedMilliseconds,
                inputTokens, outputTokens);

            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", (long)inputTokens);
            activity?.SetTag("output_tokens", (long)outputTokens);
            _logger.LogInformation(
                "AiGeneration LLM OK (trace_id={TraceId} model={Model} input={Input} output={Output})",
                traceId, modelName, inputTokens, outputTokens);

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

        // prr-046: canonical per-feature cost counter. Pricing re-resolved
        // from routing-config.yaml (may differ from local constants above if
        // YAML has been updated — fail-loud vs. stale constants).
        _featureCost.Record(
            feature: "question-generation",
            tier: "tier3",
            task: "question_generation",
            modelId: model,
            inputTokens: inputTokens,
            outputTokens: outputTokens);

        _logger.LogInformation(
            "LLM call completed: model={Model}, task={Task}, duration={DurationMs}ms, " +
            "input_tokens={InputTokens}, output_tokens={OutputTokens}, cost_usd={Cost:F6}",
            model, taskType, durationMs, inputTokens, outputTokens, cost);
    }

    // ── Batch Generation (CNT-002) ──

    public async Task<BatchGenerateResponse> BatchGenerateAsync(
        BatchGenerateRequest req, QualityGateServices.IQualityGateService qualityGate)
    {
        var count = Math.Clamp(req.Count, 1, 20);
        var min   = Math.Clamp(req.MinDifficulty, 0f, 1f);
        var max   = Math.Clamp(req.MaxDifficulty, 0f, 1f);
        if (max < min) (min, max) = (max, min);

        // ADR-0059 §15.5 structural-variant payload: build a "creative seed"
        // string from SourceContext (+ optional LaTeX). BuildPrompt detects
        // the [SOURCE-AS-CREATIVE-SEED] marker and emits the do-not-copy
        // guardrails so the LLM produces competency-equivalent variants
        // rather than near-clones.
        string? context = null;
        if (!string.IsNullOrWhiteSpace(req.SourceContext))
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[SOURCE-AS-CREATIVE-SEED]");
            sb.AppendLine(req.SourceContext.Trim());
            if (!string.IsNullOrWhiteSpace(req.SourceLatex))
            {
                sb.AppendLine();
                sb.AppendLine("Source LaTeX:");
                sb.AppendLine(req.SourceLatex.Trim());
            }
            context = sb.ToString();
        }

        var generateReq = new AiGenerateRequest(
            Subject:          req.Subject,
            Topic:            req.Topic,
            Grade:            req.Grade,
            BloomsLevel:      req.BloomsLevel,
            MinDifficulty:    min,
            MaxDifficulty:    max,
            Language:         req.Language,
            Context:          context,
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
                Error: generateResponse.Error,
                PromptUsed: generateResponse.PromptUsed,
                TemperatureUsed: generateResponse.TemperatureUsed,
                RawOutput: generateResponse.RawOutput);
        }

        var results = new List<BatchGenerateResult>();
        var drops = new List<CasDropReason>();

        // RDY-034 / ADR-0002: One scope per batch — the gate uses Marten
        // sessions so it must run inside a DI scope.
        using var casScope = _scopeFactory.CreateScope();
        var casGate = _casGateMode.CurrentMode == CasGateMode.Off
            ? null
            : casScope.ServiceProvider.GetService<ICasVerificationGate>();

        foreach (var question in generateResponse.Questions)
        {
            var questionId = Guid.NewGuid().ToString();
            var correctIndex = question.Options
                .Select((o, i) => (o, i))
                .FirstOrDefault(x => x.o.IsCorrect).i;

            // ── CAS gate call ────────────────────────────────────────────
            CasGateResult? casResult = null;
            if (casGate is not null)
            {
                var correctText = question.Options.FirstOrDefault(o => o.IsCorrect)?.Text ?? "";
                casResult = await casGate.VerifyForCreateAsync(
                    questionId, req.Subject, question.Stem, correctText, variable: null);

                if (_casGateMode.CurrentMode == CasGateMode.Enforce
                    && casResult.Outcome == CasGateOutcome.Failed)
                {
                    _logger.LogWarning(
                        "[AI_GEN_CAS_REJECT] questionId={Qid} engine={Engine} reason={Reason} latencyMs={Latency}",
                        questionId, casResult.Engine, casResult.FailureReason, casResult.LatencyMs);
                    drops.Add(new CasDropReason(
                        question.Stem, casResult.Engine,
                        casResult.FailureReason ?? "CAS contradicted authored answer",
                        casResult.LatencyMs));
                    continue; // skip — do not include in results
                }
            }

            var gateInput = new QualityGateInput(
                QuestionId:       questionId,
                Stem:             question.Stem,
                Options:          question.Options.Select(o =>
                    new QualityGateOption(o.Label, o.Text, o.IsCorrect, o.DistractorRationale))
                    .ToList(),
                CorrectOptionIndex: correctIndex,
                Subject:          req.Subject,
                Language:         req.Language,
                ClaimedBloomLevel: question.BloomsLevel,
                ClaimedDifficulty: question.Difficulty,
                Grade:            req.Grade,
                ConceptIds:       null);

            var gateResult = await qualityGate.EvaluateAsync(gateInput);
            var passed = gateResult.Decision != GateDecision.AutoRejected;

            results.Add(new BatchGenerateResult(
                question, gateResult, passed,
                CasOutcome: casResult?.Outcome.ToString(),
                CasEngine: casResult?.Engine,
                CasFailureReason: casResult?.FailureReason));
        }

        return new BatchGenerateResponse(
            Success:           true,
            Results:           results,
            TotalGenerated:    results.Count,
            PassedQualityGate: results.Count(r => r.QualityGate.Decision == GateDecision.AutoApproved),
            NeedsReview:       results.Count(r => r.QualityGate.Decision == GateDecision.NeedsReview),
            AutoRejected:      results.Count(r => r.QualityGate.Decision == GateDecision.AutoRejected),
            ModelUsed:         generateResponse.ModelUsed,
            Error:             null,
            DroppedForCasFailure: drops.Count,
            CasDropReasons:    drops,
            // PRR-322f-audit: pass through the real prompt / temperature /
            // raw model output so persistence callers can populate
            // QuestionDocument.AiGenerationState honestly. The same prompt
            // (and the same single LLM round-trip's raw output) is
            // attributed to every variant in the batch — that's accurate:
            // GenerateQuestionsAsync is one call returning N questions.
            PromptUsed:        generateResponse.PromptUsed,
            TemperatureUsed:   generateResponse.TemperatureUsed,
            RawOutput:         generateResponse.RawOutput);
    }

    // ── Template (OCR) Generation (CNT-002) ──

    public async Task<TemplateGenerateResponse> GenerateFromTemplateAsync(
        TemplateGenerateRequest req, QualityGateServices.IQualityGateService qualityGate)
    {
        if (string.IsNullOrWhiteSpace(req.OcrText))
        {
            // Surface the *currently configured* model so the SPA's error toast
            // matches the rest of the UI's "Model" field. SonnetModelId is the
            // fallback when the singleton doc has never been written.
            var doc = await LoadDocAsync().ConfigureAwait(false);
            var modelForError = string.IsNullOrWhiteSpace(doc.AnthropicModelId)
                ? SonnetModelId
                : doc.AnthropicModelId;

            return new TemplateGenerateResponse(
                Success: false,
                Results: Array.Empty<BatchGenerateResult>(),
                TotalGenerated: 0,
                PassedQualityGate: 0,
                NeedsReview: 0,
                AutoRejected: 0,
                ModelUsed: modelForError,
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
        var drops = new List<CasDropReason>();

        using var casScope = _scopeFactory.CreateScope();
        var casGate = _casGateMode.CurrentMode == CasGateMode.Off
            ? null
            : casScope.ServiceProvider.GetService<ICasVerificationGate>();

        foreach (var question in generateResponse.Questions)
        {
            var questionId = Guid.NewGuid().ToString();
            var correctIndex = question.Options
                .Select((o, i) => (o, i))
                .FirstOrDefault(x => x.o.IsCorrect).i;

            CasGateResult? casResult = null;
            if (casGate is not null)
            {
                var correctText = question.Options.FirstOrDefault(o => o.IsCorrect)?.Text ?? "";
                casResult = await casGate.VerifyForCreateAsync(
                    questionId, req.Subject, question.Stem, correctText, variable: null);

                if (_casGateMode.CurrentMode == CasGateMode.Enforce
                    && casResult.Outcome == CasGateOutcome.Failed)
                {
                    _logger.LogWarning(
                        "[AI_GEN_CAS_REJECT] questionId={Qid} engine={Engine} reason={Reason} latencyMs={Latency}",
                        questionId, casResult.Engine, casResult.FailureReason, casResult.LatencyMs);
                    drops.Add(new CasDropReason(
                        question.Stem, casResult.Engine,
                        casResult.FailureReason ?? "CAS contradicted authored answer",
                        casResult.LatencyMs));
                    continue;
                }
            }

            var gateInput = new QualityGateInput(
                QuestionId:       questionId,
                Stem:             question.Stem,
                Options:          question.Options.Select(o =>
                    new QualityGateOption(o.Label, o.Text, o.IsCorrect, o.DistractorRationale))
                    .ToList(),
                CorrectOptionIndex: correctIndex,
                Subject:          req.Subject,
                Language:         req.Language,
                ClaimedBloomLevel: question.BloomsLevel,
                ClaimedDifficulty: question.Difficulty,
                Grade:            req.Grade,
                ConceptIds:       null);

            var gateResult = await qualityGate.EvaluateAsync(gateInput);
            var passed = gateResult.Decision != GateDecision.AutoRejected;

            results.Add(new BatchGenerateResult(
                question, gateResult, passed,
                CasOutcome: casResult?.Outcome.ToString(),
                CasEngine: casResult?.Engine,
                CasFailureReason: casResult?.FailureReason));
        }

        return new TemplateGenerateResponse(
            Success:           true,
            Results:           results,
            TotalGenerated:    results.Count,
            PassedQualityGate: results.Count(r => r.QualityGate.Decision == GateDecision.AutoApproved),
            NeedsReview:       results.Count(r => r.QualityGate.Decision == GateDecision.NeedsReview),
            AutoRejected:      results.Count(r => r.QualityGate.Decision == GateDecision.AutoRejected),
            ModelUsed:         generateResponse.ModelUsed,
            Error:             null,
            DroppedForCasFailure: drops.Count,
            CasDropReasons:    drops);
    }

    // ADR-0062 Phase 1.5 — OCR cleanup pass moved to
    // Cena.Admin.Api.Ingestion.OcrTextEnhancer in 2026-05-03 to keep this
    // service's surface area focused. Endpoint
    // POST /api/admin/ingestion/items/{id}/enhance-text injects
    // IOcrTextEnhancer directly.
}
