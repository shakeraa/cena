// =============================================================================
// Cena Platform — OCR Text Enhancer (ADR-0062 Phase 1.5)
//
// Curator-initiated OCR cleanup pass. Asks Anthropic to wrap math in
// \( ... \) / \[ ... \] delimiters, restore paragraph breaks, and emit
// [[FIGURE:p<page>]] markers where the source references a figure. The
// SPA renders the output through the same renderMixedMathText path the
// curator already trusts.
//
// Cache layer (added 2026-05-03)
// ------------------------------
// Calls are deterministic at temperature=0, so identical input must not
// pay for the LLM twice. This service consults `IOcrEnhancementCache`
// (Marten document, sha256(input)-keyed, 24h absolute TTL) before
// invoking Anthropic and writes the cleaned text back on success. Cache
// hits emit a `llm_cache_hits_total` counter (no token counters) so the
// cost dashboard separates "money saved" from "money spent". Persistence
// onto the BagrutDraftPayloadDocument lives in the endpoint handler
// (QuestionConceptsEndpoints), not here — the enhancer doesn't know
// about drafts. See OcrEnhancementCache.cs for the choice rationale.
//
// Shared LLM runtime (2026-05-03 — completes the original extraction)
// -------------------------------------------------------------------
// Anthropic client cache, in-process circuit breaker (3 failures / 90s),
// and the legacy per-service meters (llm_request_duration_ms,
// llm_tokens_total, llm_cost_usd) live in IAnthropicLlmRuntime. This
// service consumes the singleton runtime so a flaky-model trip on
// question-generation also gates OCR-enhance and dashboards see one
// cohesive series per (model, task_type). Cache-hit observability
// (llm_cache_hits_total) stays here because it is OCR-enhance-specific.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    string? Error,
    // CacheHit and InputHash are populated on every Success=true path so
    // the calling endpoint can persist the cache key on the draft and
    // the SPA can reflect "the result was cached" in observability /
    // ops surfaces. Non-success responses leave both null.
    bool CacheHit = false,
    string? InputHash = null);

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
    private readonly IOcrEnhancementCache _cache;
    private readonly IAnthropicLlmRuntime _runtime;
    private readonly IActivityPropagator? _activityPropagator;

    // Cache observability — separated from token counters so finops
    // dashboards see "$ saved by cache hit" distinct from "$ spent on
    // LLM call". Tagged by task_type so this meter shape can host other
    // task types in the future (currently just ocr_text_enhance).
    // Kept here because it's OCR-enhance-specific; the legacy per-service
    // triple lives in IAnthropicLlmRuntime now.
    private readonly Counter<long> _cacheHits;

    // Routing config constants
    private const string SonnetModelId = "claude-sonnet-4-6";
    private const int MaxEnhanceTokens = 2048;

    public OcrTextEnhancer(
        ILogger<OcrTextEnhancer> logger,
        IConfiguration configuration,
        IMeterFactory meterFactory,
        IDocumentStore documentStore,
        IApiKeyCipher cipher,
        ILlmCostMetric featureCost,
        IOcrEnhancementCache cache,
        IAnthropicLlmRuntime runtime,
        IActivityPropagator? activityPropagator = null)
    {
        _logger = logger;
        _configuration = configuration;
        _documentStore = documentStore;
        _cipher = cipher;
        _featureCost = featureCost;
        _cache = cache;
        _runtime = runtime;
        _activityPropagator = activityPropagator;

        // Cache-hit counter on the same meter name as the runtime emits —
        // meter factories deduplicate by (name, version) so all the
        // Cena.Admin.LlmMetrics series stay together in the dashboard.
        var meter = meterFactory.Create("Cena.Admin.LlmMetrics", "1.0.0");
        _cacheHits = meter.CreateCounter<long>(
            "llm_cache_hits_total",
            description: "Number of cache hits that avoided an LLM call");
    }

    public async Task<EnhanceOcrTextResponse> EnhanceOcrTextAsync(
        EnhanceOcrTextRequest req,
        CancellationToken ct = default)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.OcrText))
            return new EnhanceOcrTextResponse(false, "", null, "ocrText is required.");

        // Cache lookup (sha256-keyed). Hits skip API-key resolution +
        // circuit breaker + LLM call entirely. The cache key is
        // surfaced on the response so the endpoint can persist it on
        // the draft alongside the enhanced text.
        var inputHash = _cache.ComputeKey(req.OcrText);
        var cached = await _cache.TryGetAsync(inputHash, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _cacheHits.Add(1,
                new KeyValuePair<string, object?>("task_type", "ocr_text_enhance"),
                new KeyValuePair<string, object?>("model_id", cached.ModelUsed));
            _logger.LogInformation(
                "OcrTextEnhancer cache HIT (model={Model} computedAt={ComputedAt} hash={Hash})",
                cached.ModelUsed, cached.ComputedAt, inputHash);
            return new EnhanceOcrTextResponse(
                Success: true,
                EnhancedText: cached.EnhancedText,
                ModelUsed: cached.ModelUsed,
                Error: null,
                CacheHit: true,
                InputHash: inputHash);
        }

        var doc = await LoadDocAsync(ct).ConfigureAwait(false);
        var apiKey = ResolveApiKey(doc);
        if (string.IsNullOrEmpty(apiKey))
            return new EnhanceOcrTextResponse(false, "", null,
                "No API key configured for Anthropic. Set Anthropic:ApiKey in Settings > AI Providers.");

        var modelName = string.IsNullOrWhiteSpace(doc.AnthropicModelId)
            ? SonnetModelId
            : doc.AnthropicModelId;

        try { _runtime.RequestCircuitPermission(modelName); }
        catch (CircuitOpenException ex)
        {
            return new EnhanceOcrTextResponse(false, "", modelName, ex.Message);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _runtime.GetOrCreateClient(apiKey);
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
            // Gap 30 fix: pricing per-call. OcrTextEnhancer reuses the persisted
            // AnthropicModelId or the SonnetModelId default. Match on family so
            // an admin selecting Haiku in the SPA dropdown is priced correctly.
            _runtime.EmitMetrics(modelName, "ocr_text_enhance", sw.ElapsedMilliseconds,
                inputTokens, outputTokens, ResolvePricingFor(modelName));

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
                "OcrTextEnhancer OK (trace_id={TraceId} model={Model} input={Input} output={Output} duration={DurationMs}ms hash={Hash})",
                traceId, modelName, inputTokens, outputTokens, sw.ElapsedMilliseconds, inputHash);

            _runtime.RecordSuccess(modelName);
            var enhanced = text!.Trim();

            // Best-effort cache write. A failure here must not turn a
            // successful LLM call into a failed response — the curator
            // already paid for the tokens, surface the result and let
            // the next call re-cache. Logged at warn level for SIEM.
            try
            {
                await _cache.StoreAsync(inputHash, enhanced, modelName, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx,
                    "OcrTextEnhancer cache write failed (hash={Hash}) — returning fresh result anyway",
                    inputHash);
            }

            return new EnhanceOcrTextResponse(
                Success: true,
                EnhancedText: enhanced,
                ModelUsed: modelName,
                Error: null,
                CacheHit: false,
                InputHash: inputHash);
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
            _runtime.RecordFailure(modelName);
            _logger.LogError(ex, "OCR text enhancement failed for Anthropic");
            return new EnhanceOcrTextResponse(false, "", modelName, ex.Message);
        }
    }

    // ── API-key resolution ───────────────────────────────────────────────

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

    /// <summary>
    /// Resolve per-call pricing for the configured Anthropic model. Mirrors
    /// AiGenerationService.ResolvePricingFor — kept local because the
    /// runtime intentionally accepts no default and forces every consumer
    /// to be explicit. Family-substring match keeps the SPA-dropdown values
    /// (claude-haiku-4-5-20251001, claude-sonnet-4-6, etc.) priced correctly.
    /// </summary>
    private LlmCallPricing ResolvePricingFor(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return LlmCallPricing.AnthropicSonnet4_6;
        if (modelId.Contains("haiku", StringComparison.OrdinalIgnoreCase))
            return LlmCallPricing.AnthropicHaiku4_5;
        if (modelId.Contains("sonnet", StringComparison.OrdinalIgnoreCase))
            return LlmCallPricing.AnthropicSonnet4_6;
        if (modelId.Contains("opus", StringComparison.OrdinalIgnoreCase))
            return new LlmCallPricing(15.00m, 75.00m);
        _logger.LogWarning(
            "OcrTextEnhancer: unknown model_id={Model} for pricing resolution — defaulting to Sonnet 4.6 rates.",
            modelId);
        return LlmCallPricing.AnthropicSonnet4_6;
    }
}
