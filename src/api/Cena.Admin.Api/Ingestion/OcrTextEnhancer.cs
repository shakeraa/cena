// =============================================================================
// Cena Platform — OCR Text Enhancer (ADR-0062 Phase 1.5)
//
// Curator-initiated OCR cleanup pass. Asks Anthropic to wrap math in
// \( ... \) / \[ ... \] delimiters, restore paragraph breaks, and emit
// [[FIGURE:p<page>]] markers where the source references a figure. The
// SPA renders the output through the same renderMixedMathText path the
// curator already trusts.
//
// Extracted from AiGenerationService 2026-05-03. Trade-offs:
//
// 1) ~30 LOC duplication. Anthropic client + API-key resolution + meter
//    construction are duplicated here rather than promoted to a shared
//    IAnthropicLlmRuntime — the runtime extraction is the right long-term
//    refactor (option (a) in the brief) but widens scope across
//    question-generation, settings, and connection-test seams. Picked
//    option (c) for blast-radius containment.
//
// 2) Parallel circuit breakers. The original method shared
//    AiGenerationService's in-process breaker, so a flaky-model trip on
//    question-generation also gated OCR-enhance. The extracted service
//    runs its OWN breaker — independent state, less effective coverage.
//    Acceptable trade because OCR-enhance is rare-call (curator-driven,
//    not student-traffic) and the breaker still protects the per-service
//    failure path. Promoting the breaker to IAnthropicLlmRuntime is the
//    correct fix and is explicitly out of scope this turn.
//
// 3) Parallel meters. The original method emitted both the per-feature
//    cost counter (cena_llm_call_cost_usd_total via ILlmCostMetric) and
//    the legacy per-service meters (llm_request_duration_ms,
//    llm_tokens_total, llm_cost_usd on Cena.Admin.LlmMetrics). To keep
//    operational dashboards working without a meter rename, this service
//    emits to the SAME meter name ("Cena.Admin.LlmMetrics") — meter
//    factories deduplicate by name + version, so dashboards see the same
//    series.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion;

// ── ADR-0062 Phase 1.5 — OCR cleanup pass DTOs ──
public sealed record EnhanceOcrTextRequest(string OcrText, string? SourceContext = null);

public sealed record EnhanceOcrTextResponse(
    bool Success,
    string EnhancedText,
    string? ModelUsed,
    string? Error);

/// <summary>
/// ADR-0062 Phase 1.5 — runs the OCR-cleanup pass on a Bagrut draft's
/// raw text. Reuses the same API key + cost metrics as question
/// generation. Never throws — returns Success=false on the unhappy path.
/// </summary>
public interface IOcrTextEnhancer
{
    Task<EnhanceOcrTextResponse> EnhanceOcrTextAsync(
        EnhanceOcrTextRequest request,
        CancellationToken ct = default);
}

[TaskRouting("tier3", "ocr_text_enhance")]
[FeatureTag("ocr-text-enhance")]
[PiiPreScrubbed("Admin tool — input is OCR'd Bagrut draft text reviewed by a curator. No student profile fields or student free-text reach this seam.")]
public sealed class OcrTextEnhancer : IOcrTextEnhancer
{
    private readonly ILogger<OcrTextEnhancer> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDocumentStore _documentStore;
    private readonly IApiKeyCipher _cipher;
    private readonly ILlmCostMetric _featureCost;
    private readonly IActivityPropagator? _activityPropagator;

    // Legacy per-service meters preserved on the SAME meter name as
    // AiGenerationService so existing dashboards continue to receive
    // ocr_text_enhance task_type rows without a rename.
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _tokensTotal;
    private readonly Counter<double> _costUsd;

    // Anthropic client cache — keyed on plaintext key, refreshed when the
    // operator rotates the persisted cipher.
    private AnthropicClient? _anthropicClient;
    private string? _lastApiKey;
    private readonly object _clientLock = new();

    // Local in-process circuit breaker (parallel to the one in
    // AiGenerationService — see file header trade-off #2). Mirrors the
    // 3-failure / 90s open thresholds so OCR-enhance failure storms gate
    // themselves cleanly without piling failed calls onto Anthropic.
    private int _failureCount;
    private DateTimeOffset _circuitOpenedAt;
    private bool _circuitOpen;
    private static readonly int MaxFailures = 3;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(90);
    private readonly object _cbLock = new();

    // Routing config constants (mirror the values in AiGenerationService;
    // intentionally local copies — see file header for the trade-off).
    private const string SonnetModelId = "claude-sonnet-4-6";
    private const int MaxEnhanceTokens = 2048;
    private const double CostPerInputMTok = 3.00;
    private const double CostPerOutputMTok = 15.00;

    public OcrTextEnhancer(
        ILogger<OcrTextEnhancer> logger,
        IConfiguration configuration,
        IMeterFactory meterFactory,
        IDocumentStore documentStore,
        IApiKeyCipher cipher,
        ILlmCostMetric featureCost,
        IActivityPropagator? activityPropagator = null)
    {
        _logger = logger;
        _configuration = configuration;
        _documentStore = documentStore;
        _cipher = cipher;
        _featureCost = featureCost;
        _activityPropagator = activityPropagator;

        // Same meter name as AiGenerationService — meter factories
        // deduplicate by (name, version) so dashboard queries on
        // llm_request_duration_ms / llm_tokens_total / llm_cost_usd see
        // both services' rows under their respective task_type tag.
        var meter = meterFactory.Create("Cena.Admin.LlmMetrics", "1.0.0");
        _requestDuration = meter.CreateHistogram<double>(
            "llm_request_duration_ms", unit: "ms",
            description: "LLM request duration in milliseconds");
        _tokensTotal = meter.CreateCounter<long>(
            "llm_tokens_total",
            description: "Total LLM tokens consumed");
        _costUsd = meter.CreateCounter<double>(
            "llm_cost_usd", unit: "USD",
            description: "LLM cost in USD");
    }

    public async Task<EnhanceOcrTextResponse> EnhanceOcrTextAsync(
        EnhanceOcrTextRequest req,
        CancellationToken ct = default)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.OcrText))
            return new EnhanceOcrTextResponse(false, "", null, "ocrText is required.");

        var doc = await LoadDocAsync(ct).ConfigureAwait(false);
        var apiKey = ResolveApiKey(doc);
        if (string.IsNullOrEmpty(apiKey))
            return new EnhanceOcrTextResponse(false, "", null,
                "No API key configured for Anthropic. Set Anthropic:ApiKey in Settings > AI Providers.");

        var modelName = string.IsNullOrWhiteSpace(doc.AnthropicModelId)
            ? SonnetModelId
            : doc.AnthropicModelId;

        try { RequestCircuitPermission(modelName); }
        catch (CircuitOpenException ex)
        {
            return new EnhanceOcrTextResponse(false, "", modelName, ex.Message);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var client = GetOrCreateClient(apiKey);
            var systemPrompt =
                "You are an OCR-cleanup assistant for Israeli Bagrut math papers. " +
                "Input: raw OCR text from a Bagrut question (Hebrew/Arabic with embedded math). " +
                "Output rules: " +
                "1) Wrap inline math in \\( ... \\) and display math in \\[ ... \\]. " +
                "2) Preserve Hebrew/Arabic prose verbatim and keep RTL paragraph flow. " +
                "3) Restore paragraph breaks; do not invent content. " +
                "4) Where the source clearly references a figure (e.g. 'see graph', 'in the diagram'), " +
                "insert a marker on its own line: [[FIGURE:p<page>]] when a page is given, else [[FIGURE]]. " +
                "5) Return ONLY the cleaned text. No commentary.";
            var userPrompt = req.OcrText;

            var createParams = new MessageCreateParams
            {
                Model = modelName,
                MaxTokens = MaxEnhanceTokens,
                Temperature = 0.0f,
                System = new List<TextBlockParam> { new TextBlockParam { Text = systemPrompt } },
                Messages = new List<MessageParam>
                {
                    new MessageParam { Role = "user", Content = userPrompt }
                },
            };

            var traceId = _activityPropagator?.GetTraceId();
            using var activity = _activityPropagator?.StartLlmActivity("ocr_text_enhance");
            activity?.SetTag("trace_id", traceId);
            activity?.SetTag("task", "ocr_text_enhance");
            activity?.SetTag("tier", "tier3");
            activity?.SetTag("model_id", modelName);

            var response = await client.Messages.Create(createParams);
            sw.Stop();

            var inputTokens = response.Usage.InputTokens;
            var outputTokens = response.Usage.OutputTokens;
            EmitMetrics(modelName, "ocr_text_enhance", sw.ElapsedMilliseconds,
                inputTokens, outputTokens);

            // prr-046: canonical per-feature cost counter (cena_llm_call_cost_usd_total)
            _featureCost.Record(
                feature: "ocr-text-enhance",
                tier: "tier3",
                task: "ocr_text_enhance",
                modelId: modelName,
                inputTokens: inputTokens,
                outputTokens: outputTokens);

            string? text = null;
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t))
                {
                    text = t.Text;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                activity?.SetTag("outcome", "empty");
                // Empty response is an Anthropic-side anomaly, not a
                // local failure — don't trip the breaker on it. Mirrors
                // the original behavior where the empty branch returned
                // without RecordFailure.
                return new EnhanceOcrTextResponse(false, "", modelName,
                    "Anthropic returned an empty response.");
            }

            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", (long)inputTokens);
            activity?.SetTag("output_tokens", (long)outputTokens);
            _logger.LogInformation(
                "OcrTextEnhancer OK (trace_id={TraceId} model={Model} input={Input} output={Output} duration={DurationMs}ms)",
                traceId, modelName, inputTokens, outputTokens, sw.ElapsedMilliseconds);

            RecordSuccess(modelName);
            return new EnhanceOcrTextResponse(true, text!.Trim(), modelName, null);
        }
        catch (CircuitOpenException ex)
        {
            // The breaker tripped between RequestCircuitPermission and
            // the outbound call (e.g. concurrent failure recorded by
            // another in-flight request). Surface the wait window.
            sw.Stop();
            return new EnhanceOcrTextResponse(false, "", modelName, ex.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure(modelName);
            _logger.LogError(ex, "OCR text enhancement failed for Anthropic");
            return new EnhanceOcrTextResponse(false, "", modelName, ex.Message);
        }
    }

    // ── Circuit breaker (parallel to AiGenerationService — see file header) ──

    private void RequestCircuitPermission(string modelName)
    {
        lock (_cbLock)
        {
            if (!_circuitOpen) return;

            if (DateTimeOffset.UtcNow - _circuitOpenedAt >= OpenDuration)
            {
                _logger.LogInformation(
                    "OcrTextEnhancer breaker half-open for {Model}, allowing probe", modelName);
                _circuitOpen = false;
                _failureCount = 0;
                return;
            }

            var retryAfter = OpenDuration - (DateTimeOffset.UtcNow - _circuitOpenedAt);
            throw new CircuitOpenException(
                $"Circuit breaker OPEN for model {modelName}. Retry after {retryAfter.TotalSeconds:F0}s.");
        }
    }

    private void RecordSuccess(string _)
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
            _logger.LogWarning(
                "OcrTextEnhancer LLM failure for {Model}. Count={Count}/{Max}",
                modelName, _failureCount, MaxFailures);

            if (_failureCount >= MaxFailures)
            {
                _circuitOpen = true;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "OcrTextEnhancer breaker OPENED for {Model}. Failures={Count}, OpenDuration={Duration}s",
                    modelName, _failureCount, OpenDuration.TotalSeconds);
            }
        }
    }

    // ── Metrics (mirrors AiGenerationService.EmitMetrics) ────────────────

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
    }

    // ── API-key resolution (local copy; see file header) ─────────────────

    private async Task<AiSettingsDocument> LoadDocAsync(CancellationToken ct)
    {
        try
        {
            await using var session = _documentStore.QuerySession();
            var loaded = await session.LoadAsync<AiSettingsDocument>(
                AiSettingsDocument.SingletonId, ct).ConfigureAwait(false);
            return loaded ?? new AiSettingsDocument();
        }
        catch (Marten.Exceptions.MartenCommandException ex)
            when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
        {
            // First-run cold start: settings table not yet auto-created.
            // Fall back to IConfiguration via ResolveApiKey.
            _logger.LogInformation(
                "AiSettingsDocument table not yet created — using configuration fallback for OCR enhance");
            return new AiSettingsDocument();
        }
    }

    private string? ResolveApiKey(AiSettingsDocument doc)
    {
        if (!string.IsNullOrEmpty(doc.AnthropicApiKeyCipher))
        {
            if (_cipher.TryDecryptFromWire(doc.AnthropicApiKeyCipher, out var plaintext))
                return plaintext;

            _logger.LogError(
                "[SIEM] Failed to decrypt persisted Anthropic API key for OCR enhance — master key may have rotated, " +
                "or the cipher blob is corrupt. Falling back to IConfiguration.");
        }

        var fromConfig = _configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }

    private AnthropicClient GetOrCreateClient(string apiKey)
    {
        lock (_clientLock)
        {
            if (_anthropicClient is not null && _lastApiKey == apiKey)
                return _anthropicClient;

            _anthropicClient = new AnthropicClient(new ClientOptions
            {
                ApiKey = apiKey,
                MaxRetries = 0,
            });
            _lastApiKey = apiKey;
            return _anthropicClient;
        }
    }
}
