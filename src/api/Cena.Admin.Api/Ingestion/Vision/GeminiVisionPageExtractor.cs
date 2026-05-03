// =============================================================================
// Cena Platform — GeminiVisionPageExtractor (vision-extractor branch)
//
// Single-call vision-LLM extractor for one rendered Bagrut PDF page. Uses
// Gemini's tool-use parameter to constrain the model to return a structured
// JSON of `{ promptText, latex, figures[] }` matching the closed-set tool
// schema. Replaces the brittle multi-layer cascade — see the file header on
// IBagrutPageVisionExtractor for the full rationale.
//
// Wiring discipline (mirrors LlmBagrutQuestionSegmenter exactly):
//   - [TaskRouting] + [FeatureTag] + [PiiPreScrubbed] attributes so the
//     architecture tests CostMetricEmittedTest + EveryLlmServiceEmitsTraceIdTest
//     can verify the call site participates in the standard observability
//     contract.
//   - IModelResolver is the only seam from which the model id flows. No
//     hardcoded gemini-2.0-flash anywhere; the routing-config.yaml row is
//     the single source of truth, the curator's per-task override panel
//     overrides on top.
//   - IActivityPropagator stamps trace_id on the activity tag + log lines.
//   - ILlmCostMetric records per-call cost on the success path.
//
// Fail-open behaviour (every catch path returns null so the caller falls
// back to the legacy cascade):
//   - feature flag OFF              → never called by the ingestion service
//   - API key missing               → return null (logged at DEBUG once)
//   - SDK/HTTP throws               → return null with WARN log
//   - tool input null/malformed     → return null with WARN log
//   - empty extraction              → return null with WARN log
//
// Why a separate breaker (vs IAnthropicLlmRuntime):
//   IAnthropicLlmRuntime is Anthropic-specific (it owns the SDK client
//   cache + per-Anthropic-model breaker). Gemini is a different provider;
//   tying its trips into IAnthropicLlmRuntime would conflate two trip
//   surfaces. We keep the in-process breaker simple here — three
//   consecutive failures within 60s open the gate for 30s. It is a small,
//   bounded surface; if vision usage grows, an extracted IVisionLlmRuntime
//   class is the natural next step.
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion.Vision;

/// <summary>
/// Configuration for <see cref="GeminiVisionPageExtractor"/>. Bind from
/// <c>Ocr:GeminiVision</c> (separate from the legacy <c>Ocr:Gemini</c> used
/// by the cascade's Layer-4b rescue runner so a curator can swap one model
/// without affecting the other).
/// </summary>
public sealed class GeminiVisionPageExtractorOptions
{
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>
    /// API key for generativelanguage.googleapis.com. Resolved from
    /// configuration at runtime; null/empty means "not configured" and the
    /// extractor returns null (caller falls back to the legacy cascade).
    /// </summary>
    public string? ApiKey { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Hard cap on the page PNG size sent inline to Gemini. 200-DPI A4
    /// pages typically render to 1.5-3 MB; 12 MB headroom covers
    /// double-page A3 scans without hitting Gemini's 20 MB inline-data
    /// per-request soft cap.
    /// </summary>
    public int MaxImageBytes { get; init; } = 12_000_000;
}

[TaskRouting("tier3", "bagrut_page_extraction")]
[FeatureTag("content-extraction")]
[PiiPreScrubbed("Admin tool — input is the rendered PNG of a Ministry-published Bagrut PDF page. Bagrut PDFs are exam booklets with no student-profile fields and no student free-text. The model output is structured tool-use JSON of {promptText, latex, figures[]} that lands in BagrutDraftPayloadDocument for curator review — never a student-facing surface unless the curator explicitly authors a recreation.")]
public sealed class GeminiVisionPageExtractor : IBagrutPageVisionExtractor
{
    public const string TaskName = "bagrut_page_extraction";
    private const string FeatureName = "content-extraction";
    private const string Tier = "tier3";

    private const int MaxOutputTokens = 4096;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// System prompt — kept terse + deterministic. Hebrew preservation,
    /// math-as-LaTeX, figure-as-bbox-in-image-coords are the only three
    /// rules. Temperature pinned to 0 so the same page renders identically
    /// across reruns.
    /// </summary>
    private const string SystemPrompt =
        "You are extracting one page of a Hebrew (or Arabic) high-school Bagrut math exam. " +
        "Read the rendered page image and emit ONE call to the extract_bagrut_page tool with: " +
        "(1) promptText — the question text in source/reading order, preserving Hebrew/Arabic " +
        "Unicode characters verbatim. Mathematical expressions appearing INLINE inside the prose " +
        "are wrapped in single dollar signs as LaTeX (e.g. \"$x^2 + 3x = 0$\"); display equations " +
        "are wrapped in double dollar signs ($$...$$). " +
        "(2) latex — concatenated standalone LaTeX equations from the page in document order, " +
        "joined by newline. Use empty string when the page has no math. " +
        "(3) figures — every diagram, chart, plot, geometric drawing, or table on the page, " +
        "with the bounding box in IMAGE PIXEL COORDINATES (origin top-left). Set kind to one of " +
        "'diagram', 'chart', 'table'. altText is a short caption-like description in English. " +
        "If the page contains no figures, return an empty array. " +
        "(4) confidence — your overall confidence in 0..1 that the extraction is faithful. " +
        "Return text only via the tool; never as a free-text response.";

    private readonly HttpClient _http;
    private readonly IOptions<GeminiVisionPageExtractorOptions> _opts;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiVisionPageExtractor> _logger;
    private readonly IModelResolver? _modelResolver;
    private readonly IActivityPropagator? _activityPropagator;
    private readonly ILlmCostMetric? _featureCost;

    /// <summary>
    /// IConfiguration key for the feature flag that gates this extractor.
    /// Default OFF in prod compose; default ON in hot-reload overlay so
    /// curators can validate output before flipping prod.
    /// </summary>
    public const string EnabledFlagKey = "Cena:Ingestion:BagrutVisionExtractorEnabled";

    // ── In-process breaker (3 failures within 60s → 30s open) ─────────────
    // Bounded, simple, no global state across hosts. Vision-extractor traffic
    // is curator-driven (one PDF upload per click); we'd rather suppress
    // outbound calls during a Gemini outage than burn API quota on retries.
    private const int BreakerThreshold = 3;
    private static readonly TimeSpan BreakerWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BreakerCooldown = TimeSpan.FromSeconds(30);
    private long _failureWindowStartTicks;
    private int _failureCount;
    private long _breakerOpenUntilTicks;

    public GeminiVisionPageExtractor(
        HttpClient http,
        IOptions<GeminiVisionPageExtractorOptions> opts,
        IConfiguration configuration,
        ILogger<GeminiVisionPageExtractor> logger,
        IModelResolver? modelResolver = null,
        IActivityPropagator? activityPropagator = null,
        ILlmCostMetric? featureCost = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _opts = opts;
        _configuration = configuration;
        _logger = logger;
        _modelResolver = modelResolver;
        _activityPropagator = activityPropagator;
        _featureCost = featureCost;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_opts.Value.BaseUrl);
        if (_http.Timeout == Timeout.InfiniteTimeSpan ||
            _http.Timeout > _opts.Value.RequestTimeout)
        {
            _http.Timeout = _opts.Value.RequestTimeout;
        }
    }

    public async Task<BagrutPageExtraction?> ExtractAsync(
        ReadOnlyMemory<byte> pagePngBytes,
        int pageNumber,
        string pdfId,
        CancellationToken ct = default)
    {
        if (pagePngBytes.IsEmpty) return null;
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfId);

        // Gate 0: feature flag.
        if (!IsFlagEnabled())
        {
            return null;
        }

        // Gate 1: API key. Source-of-truth is the bound options; fall back
        // to IConfiguration so a host that wires from secrets-manager-only
        // (no static appsettings) still works.
        var apiKey = _opts.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _configuration["Ocr:GeminiVision:ApiKey"]
                  ?? _configuration["Ocr:Gemini:ApiKey"];
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug(
                "GeminiVisionPageExtractor: no Gemini API key configured (trace_id={TraceId} pdf={PdfId} page={Page}) — falling back to legacy cascade",
                SafeTraceId(), pdfId, pageNumber);
            return null;
        }

        // Gate 2: image size cap.
        if (pagePngBytes.Length > _opts.Value.MaxImageBytes)
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: page PNG exceeds MaxImageBytes (trace_id={TraceId} pdf={PdfId} page={Page} size={Size}) — falling back to legacy cascade",
                SafeTraceId(), pdfId, pageNumber, pagePngBytes.Length);
            return null;
        }

        // Gate 3: in-process breaker.
        if (IsBreakerOpen())
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: breaker open (trace_id={TraceId} pdf={PdfId} page={Page}) — falling back to legacy cascade",
                SafeTraceId(), pdfId, pageNumber);
            return null;
        }

        // Resolve model id via ModelResolver. When the resolver is missing
        // (test scaffolding) or fails to resolve, fall back rather than
        // hardcoding a default — the resolver IS the seam.
        if (_modelResolver is null)
        {
            _logger.LogDebug(
                "GeminiVisionPageExtractor: no IModelResolver wired (trace_id={TraceId} pdf={PdfId} page={Page}) — falling back to legacy cascade",
                SafeTraceId(), pdfId, pageNumber);
            return null;
        }
        string modelId;
        try
        {
            modelId = await _modelResolver.ResolveModelForTaskAsync(TaskName, ct).ConfigureAwait(false);
        }
        catch (ModelNotConfiguredException ex)
        {
            _logger.LogError(ex,
                "GeminiVisionPageExtractor: ModelResolver could not resolve task='{Task}' (trace_id={TraceId} pdf={PdfId} page={Page}) — falling back to legacy cascade",
                TaskName, SafeTraceId(), pdfId, pageNumber);
            return null;
        }

        // The resolver may return a non-Gemini model id when a curator has
        // set a Claude override on the per-task panel. Claude Vision
        // routing for this seam is a follow-up branch; for now we fall
        // back to legacy cascade with a WARN so the curator sees a clear
        // log line rather than a silent regression.
        if (!modelId.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: resolver returned non-Gemini model_id='{Model}' for task='{Task}' (trace_id={TraceId} pdf={PdfId} page={Page}) — Claude Vision routing is a follow-up; falling back to legacy cascade",
                modelId, TaskName, SafeTraceId(), pdfId, pageNumber);
            return null;
        }

        var sw = Stopwatch.StartNew();
        var traceId = _activityPropagator?.GetTraceId();
        using var activity = _activityPropagator?.StartLlmActivity(TaskName);
        activity?.SetTag("trace_id", traceId);
        activity?.SetTag("task", TaskName);
        activity?.SetTag("tier", Tier);
        activity?.SetTag("model_id", modelId);
        activity?.SetTag("pdf_id", pdfId);
        activity?.SetTag("page", (long)pageNumber);

        try
        {
            var result = await CallGeminiAsync(
                apiKey!, modelId, pagePngBytes, pageNumber, pdfId, traceId, ct).ConfigureAwait(false);
            sw.Stop();

            if (result is null)
            {
                activity?.SetTag("outcome", "null_extraction");
                RecordSuccess(); // Gemini reachable, just no usable output
                return null;
            }

            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", result.InputTokens);
            activity?.SetTag("output_tokens", result.OutputTokens);
            activity?.SetTag("figure_count", (long)result.Extraction.Figures.Count);

            // Cost emission on the success path. Gemini pricing must have
            // a row under routing-config.yaml § models — fail-loud would be
            // a finops bug we want visible (LlmPricingTable.Resolve throws
            // InvalidOperationException), but we catch and degrade because
            // a missing pricing row should not fail an ingestion that
            // otherwise succeeded.
            try
            {
                _featureCost?.Record(
                    feature: FeatureName,
                    tier: Tier,
                    task: TaskName,
                    modelId: modelId,
                    inputTokens: result.InputTokens,
                    outputTokens: result.OutputTokens);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "GeminiVisionPageExtractor: cost metric emit failed (trace_id={TraceId} pdf={PdfId} page={Page}) — extraction is still good; finops should add a pricing row",
                    traceId, pdfId, pageNumber);
            }

            _logger.LogInformation(
                "GeminiVisionPageExtractor OK (trace_id={TraceId} pdf={PdfId} page={Page} duration_ms={DurationMs} input_tokens={InputTokens} output_tokens={OutputTokens} figures={Figures} confidence={Conf:F2})",
                traceId, pdfId, pageNumber, sw.ElapsedMilliseconds,
                result.InputTokens, result.OutputTokens,
                result.Extraction.Figures.Count, result.Extraction.Confidence);

            RecordSuccess();
            return result.Extraction;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation(
                "GeminiVisionPageExtractor: cancellation requested (trace_id={TraceId} pdf={PdfId} page={Page} duration_ms={DurationMs})",
                traceId, pdfId, pageNumber, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure();
            activity?.SetTag("outcome", "error");
            _logger.LogWarning(ex,
                "GeminiVisionPageExtractor: Gemini call failed (trace_id={TraceId} pdf={PdfId} page={Page} duration_ms={DurationMs}) — falling back to legacy cascade",
                traceId, pdfId, pageNumber, sw.ElapsedMilliseconds);
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Gemini call
    // -------------------------------------------------------------------------
    private async Task<GeminiCallResult?> CallGeminiAsync(
        string apiKey,
        string modelId,
        ReadOnlyMemory<byte> pageBytes,
        int pageNumber,
        string pdfId,
        string? traceId,
        CancellationToken ct)
    {
        var payload = new GenerateContentRequest(
            SystemInstruction: new Content(
                Parts: new Part[] { new Part(Text: SystemPrompt) },
                Role: null),
            Contents: new[]
            {
                new Content(
                    Parts: new Part[]
                    {
                        new Part(InlineData: new InlineData(
                            MimeType: "image/png",
                            Data: Convert.ToBase64String(pageBytes.Span))),
                        new Part(Text:
                            $"Extract page {pageNumber} of pdfId {pdfId}. " +
                            "Call extract_bagrut_page exactly once."),
                    },
                    Role: "user"),
            },
            GenerationConfig: new GenerationConfig(Temperature: 0.0, MaxOutputTokens: MaxOutputTokens),
            Tools: new[] { new ToolGroup(new[] { ExtractBagrutPageDeclaration }) },
            ToolConfig: new ToolConfig(new ToolCallingConfig(Mode: "ANY")));

        var path = $"models/{Uri.EscapeDataString(modelId)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        using var response = await _http.PostAsJsonAsync(path, payload, JsonOpts, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var bodyTail = await SafeReadBodyTailAsync(response, ct).ConfigureAwait(false);
            _logger.LogWarning(
                "GeminiVisionPageExtractor: HTTP {Status} (trace_id={TraceId} pdf={PdfId} page={Page} body_tail={Tail})",
                (int)response.StatusCode, traceId, pdfId, pageNumber, bodyTail);
            // HTTP non-success counts as a breaker failure.
            throw new HttpRequestException($"Gemini HTTP {(int)response.StatusCode}");
        }

        GenerateContentResponse? body;
        try
        {
            body = await response.Content
                .ReadFromJsonAsync<GenerateContentResponse>(JsonOpts, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "GeminiVisionPageExtractor: response JSON malformed (trace_id={TraceId} pdf={PdfId} page={Page})",
                traceId, pdfId, pageNumber);
            return null;
        }

        if (body?.Candidates is null || body.Candidates.Length == 0)
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: response had no candidates (trace_id={TraceId} pdf={PdfId} page={Page})",
                traceId, pdfId, pageNumber);
            return null;
        }

        // Find the toolCall on the first candidate. Gemini may emit text
        // parts alongside or instead; we prefer the toolCall and silently
        // ignore the text (per the system prompt the model should never
        // use text-only).
        var toolArgs = ExtractToolArgs(body.Candidates[0]);
        if (toolArgs is null)
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: no tool call in response (trace_id={TraceId} pdf={PdfId} page={Page} finishReason={Finish})",
                traceId, pdfId, pageNumber, body.Candidates[0].FinishReason ?? "?");
            return null;
        }

        var extraction = ParseExtraction(toolArgs, traceId, pdfId, pageNumber);
        if (extraction is null) return null;

        long inputTokens = body.UsageMetadata?.PromptTokenCount ?? 0;
        long outputTokens = body.UsageMetadata?.CandidatesTokenCount ?? 0;

        return new GeminiCallResult(extraction, inputTokens, outputTokens);
    }

    private BagrutPageExtraction? ParseExtraction(
        IReadOnlyDictionary<string, JsonElement> args,
        string? traceId,
        string pdfId,
        int pageNumber)
    {
        // promptText is required.
        if (!args.TryGetValue("promptText", out var promptEl)
            || promptEl.ValueKind != JsonValueKind.String)
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: tool args missing/invalid promptText (trace_id={TraceId} pdf={PdfId} page={Page})",
                traceId, pdfId, pageNumber);
            return null;
        }
        var promptText = promptEl.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            _logger.LogWarning(
                "GeminiVisionPageExtractor: tool args promptText is whitespace-only (trace_id={TraceId} pdf={PdfId} page={Page})",
                traceId, pdfId, pageNumber);
            return null;
        }

        string? latex = null;
        if (args.TryGetValue("latex", out var latexEl) && latexEl.ValueKind == JsonValueKind.String)
        {
            var raw = latexEl.GetString();
            latex = string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        var figures = new List<DetectedFigure>();
        if (args.TryGetValue("figures", out var figEl) && figEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in figEl.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                if (!f.TryGetProperty("x", out var xEl) || !xEl.TryGetDouble(out var x)) continue;
                if (!f.TryGetProperty("y", out var yEl) || !yEl.TryGetDouble(out var y)) continue;
                if (!f.TryGetProperty("width", out var wEl) || !wEl.TryGetDouble(out var w)) continue;
                if (!f.TryGetProperty("height", out var hEl) || !hEl.TryGetDouble(out var h)) continue;
                if (w <= 0 || h <= 0) continue;

                var kind = "figure";
                if (f.TryGetProperty("kind", out var kEl) && kEl.ValueKind == JsonValueKind.String)
                    kind = kEl.GetString() ?? "figure";

                string? alt = null;
                if (f.TryGetProperty("altText", out var altEl) && altEl.ValueKind == JsonValueKind.String)
                {
                    var raw = altEl.GetString();
                    alt = string.IsNullOrWhiteSpace(raw) ? null : raw;
                }

                figures.Add(new DetectedFigure(x, y, w, h, kind, alt));
            }
        }

        double confidence = 0.85;
        if (args.TryGetValue("confidence", out var confEl)
            && confEl.ValueKind == JsonValueKind.Number
            && confEl.TryGetDouble(out var c))
        {
            confidence = Math.Clamp(c, 0.0, 1.0);
        }

        return new BagrutPageExtraction(
            PromptText: promptText,
            Latex: latex,
            Figures: figures,
            Confidence: confidence);
    }

    private static IReadOnlyDictionary<string, JsonElement>? ExtractToolArgs(Candidate candidate)
    {
        var parts = candidate.Content?.Parts;
        if (parts is null) return null;
        foreach (var p in parts)
        {
            if (p.ToolCall is null) continue;
            if (!string.Equals(p.ToolCall.Name, "extract_bagrut_page", StringComparison.Ordinal))
                continue;
            if (p.ToolCall.Args.ValueKind != JsonValueKind.Object) return null;
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in p.ToolCall.Args.EnumerateObject())
                dict[prop.Name] = prop.Value;
            return dict;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Breaker (in-process)
    // -------------------------------------------------------------------------
    private bool IsBreakerOpen()
    {
        var now = DateTime.UtcNow.Ticks;
        var openUntil = Interlocked.Read(ref _breakerOpenUntilTicks);
        return now < openUntil;
    }

    private void RecordSuccess()
    {
        Interlocked.Exchange(ref _failureCount, 0);
        Interlocked.Exchange(ref _failureWindowStartTicks, 0);
    }

    private void RecordFailure()
    {
        var now = DateTime.UtcNow.Ticks;
        var windowStart = Interlocked.Read(ref _failureWindowStartTicks);
        if (windowStart == 0 || now - windowStart > BreakerWindow.Ticks)
        {
            Interlocked.Exchange(ref _failureWindowStartTicks, now);
            Interlocked.Exchange(ref _failureCount, 1);
            return;
        }
        var failures = Interlocked.Increment(ref _failureCount);
        if (failures >= BreakerThreshold)
        {
            Interlocked.Exchange(ref _breakerOpenUntilTicks, now + BreakerCooldown.Ticks);
        }
    }

    // -------------------------------------------------------------------------
    // Misc
    // -------------------------------------------------------------------------
    private bool IsFlagEnabled()
    {
        var raw = _configuration[EnabledFlagKey];
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private string SafeTraceId()
    {
        try { return _activityPropagator?.GetTraceId() ?? "no-trace"; }
        catch { return "no-trace"; }
    }

    private static async Task<string> SafeReadBodyTailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var s = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 200 ? s : s[..200];
        }
        catch { return string.Empty; }
    }

    // -------------------------------------------------------------------------
    // Tool schema (closed-set)
    //
    // Static-init ORDER MATTERS: the JsonDocument must be parsed BEFORE
    // ExtractBagrutPageDeclaration captures its .RootElement. C# initialises
    // static fields in textual order within a class — moving the schema
    // above the declaration ensures ExtractBagrutPageDeclaration sees a
    // valid JsonElement, not the default(JsonElement) struct.
    // -------------------------------------------------------------------------

    // Parse the schema once and HOLD the JsonDocument for the process
    // lifetime so .RootElement stays valid (a Parse(...).RootElement
    // pattern lets the document GC and the element becomes "current state
    // of the object" invalid the next time JsonContent.Create serialises
    // it asynchronously).
    private static readonly JsonDocument ParametersSchemaDoc = JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "promptText": {
              "type": "string",
              "description": "The full question text in source/reading order. Hebrew/Arabic verbatim. Math inside the prose is wrapped in $...$ (inline) or $$...$$ (display)."
            },
            "latex": {
              "type": "string",
              "description": "Concatenated standalone LaTeX equations from the page in document order, joined by newline. Empty string when the page has no math."
            },
            "figures": {
              "type": "array",
              "description": "Diagrams, charts, plots, drawings, and tables on the page. Empty array when none.",
              "items": {
                "type": "object",
                "properties": {
                  "x":      { "type": "number", "description": "Top-left X in image pixel coords (origin top-left)." },
                  "y":      { "type": "number", "description": "Top-left Y in image pixel coords." },
                  "width":  { "type": "number", "description": "Bounding-box width in image pixel coords." },
                  "height": { "type": "number", "description": "Bounding-box height in image pixel coords." },
                  "kind":   { "type": "string", "description": "One of: 'diagram', 'chart', 'table'." },
                  "altText":{ "type": "string", "description": "Short caption-like description in English." }
                },
                "required": ["x","y","width","height","kind"]
              }
            },
            "confidence": {
              "type": "number",
              "description": "Self-reported confidence 0..1 that the extraction is faithful."
            }
          },
          "required": ["promptText","figures"]
        }
        """);
    private static readonly JsonElement ParametersSchema = ParametersSchemaDoc.RootElement;

    private static readonly ToolDeclaration ExtractBagrutPageDeclaration = new(
        Name: "extract_bagrut_page",
        Description:
            "Emit the structured extraction of one Bagrut math exam page: " +
            "promptText (Hebrew/Arabic source-order, math inline-LaTeX), " +
            "latex (standalone equations joined by newline), " +
            "figures (bbox in image pixel coords + kind + altText), confidence.",
        Parameters: ParametersSchema);

    // -------------------------------------------------------------------------
    // Wire DTOs (Gemini /v1beta generateContent)
    // -------------------------------------------------------------------------
    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("system_instruction")] Content? SystemInstruction,
        [property: JsonPropertyName("contents")]           Content[] Contents,
        [property: JsonPropertyName("generationConfig")]   GenerationConfig? GenerationConfig,
        [property: JsonPropertyName("tools")]              ToolGroup[]? Tools,
        [property: JsonPropertyName("tool_config")]        ToolConfig? ToolConfig);

    private sealed record Content(
        [property: JsonPropertyName("parts")] Part[] Parts,
        [property: JsonPropertyName("role")]  string? Role);

    private sealed record Part(
        [property: JsonPropertyName("text")]         string? Text = null,
        [property: JsonPropertyName("inlineData")]   InlineData? InlineData = null,
        [property: JsonPropertyName("functionCall")] ToolCall? ToolCall = null);

    private sealed record InlineData(
        [property: JsonPropertyName("mimeType")] string MimeType,
        [property: JsonPropertyName("data")]     string Data);

    private sealed record ToolCall(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("args")] JsonElement Args);

    private sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")]     double? Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens);

    private sealed record ToolGroup(
        [property: JsonPropertyName("function_declarations")] ToolDeclaration[] Declarations);

    private sealed record ToolDeclaration(
        [property: JsonPropertyName("name")]        string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")]  JsonElement Parameters);

    private sealed record ToolConfig(
        [property: JsonPropertyName("function_calling_config")] ToolCallingConfig FunctionCallingConfig);

    private sealed record ToolCallingConfig(
        [property: JsonPropertyName("mode")] string Mode);

    private sealed record GenerateContentResponse(
        [property: JsonPropertyName("candidates")]    Candidate[]? Candidates,
        [property: JsonPropertyName("usageMetadata")] UsageMetadata? UsageMetadata);

    private sealed record Candidate(
        [property: JsonPropertyName("content")]      Content? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);

    private sealed record UsageMetadata(
        [property: JsonPropertyName("promptTokenCount")]     long? PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] long? CandidatesTokenCount);

    private sealed record GeminiCallResult(
        BagrutPageExtraction Extraction,
        long InputTokens,
        long OutputTokens);
}
