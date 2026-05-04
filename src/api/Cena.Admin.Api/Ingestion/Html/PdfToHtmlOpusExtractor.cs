// =============================================================================
// Cena Platform — PdfToHtmlOpusExtractor (2026-05-04, t_1c57e7389cb4)
//
// SINGLE Anthropic Opus 4.7 call: full PDF → self-contained HTML document.
// Hebrew RTL preserved, math rendered as HTML sup/sub/fraction divs (NOT
// LaTeX), every figure recreated as inline <svg>. The user validated this
// recipe directly against claude.ai on corpus/tests/35581-q.pdf and got the
// gold-standard output on the first call — so the system prompt below is
// kept VERBATIM from their tested recipe. Tightening or loosening the
// rubric breaks the user-validated quality bar.
//
// Why this replaces (not removes) the multi-step paths:
//   - Earlier paths fed broken intermediate representations to the LLM and
//     got hallucinated stacked fractions back: Poppler `pdftotext -layout`
//     emits stacked fractions as two consecutive lines, the LLM column-
//     drifts when reading them text-only. The image-block-with-Sonnet path
//     fixed *that* failure mode but still flowed through a degraded
//     representation (one rendered PNG per page) and required carrying a
//     visual-ground-truth directive in the prompt.
//   - Going direct to Opus 4.7 with the original PDF is the right level of
//     abstraction: the model sees the document as Anthropic intends, and
//     adaptive extended thinking produces the figure-by-figure SVG
//     reconstruction the user-tested recipe validated.
//   - Both earlier paths still ship and are still useful for cheaper /
//     faster curator workflows. /render-html is additive: a curator
//     triggers it explicitly when they want the high-fidelity HTML view.
//
// Wiring discipline (mirrors LlmBagrutQuestionSegmenter / OcrTextEnhancer):
//   - [TaskRouting("opus", "pdf_to_html")] + [FeatureTag("pdf-to-html")] +
//     [PiiPreScrubbed(...)] for the architecture-test ratchets
//     (CostMetricEmittedTest + EveryLlmServiceEmitsTraceIdTest).
//   - IModelResolver is the ONLY seam from which the model id flows.
//     Defaults to claude-opus-4-7 via routing-config.yaml § default_model_
//     by_task[pdf_to_html]; curators can override via the per-task panel.
//   - IActivityPropagator stamps trace_id on the activity tag + log lines.
//   - ILlmCostMetric records per-call cost on the success path.
//   - IAnthropicLlmRuntime owns the SDK client cache + circuit breaker;
//     trips from question-generation also gate this extractor.
//
// Failure semantics:
//   - Never throws to the caller. Every failure path returns
//     PdfToHtmlResponse with Success=false + populated Error so the
//     /render-html endpoint maps cleanly to a 400 with a structured
//     CenaError.
//   - The ONE exception is OperationCanceledException when ct is the
//     cancellation source — propagated up so the caller (HTTP request
//     pipeline) can short-circuit. Mirrors LlmBagrutQuestionSegmenter.
// =============================================================================

using System.Diagnostics;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Cena.Infrastructure.Security;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion.Html;

[TaskRouting("opus", "pdf_to_html")]
[FeatureTag("pdf-to-html")]
[PiiPreScrubbed("Admin tool — input is a Bagrut source PDF (Ministry-published exam booklet). Bagrut PDFs are public reference documents with no student profile fields and no student free-text. Output is a curator-facing HTML rendering for the In-Review board; never reaches a student surface unless the curator explicitly authors a recreation.")]
public sealed class PdfToHtmlOpusExtractor : IPdfToHtmlExtractor
{
    /// <summary>
    /// Canonical task name routed through <see cref="IModelResolver"/>.
    /// Matches contracts/llm/routing-config.yaml § default_model_by_task:.
    /// </summary>
    public const string TaskName = "pdf_to_html";

    private const string FeatureName = "pdf-to-html";
    private const string Tier = "opus";

    /// <summary>
    /// Hard cap on output tokens — matches the user's recipe verbatim.
    /// A 6-page Bagrut renders to roughly 8–15k tokens of HTML; 32k gives
    /// generous headroom for densely figured pages without paying for
    /// runaway generation.
    /// </summary>
    public const int MaxHtmlTokens = 32_000;

    /// <summary>
    /// System prompt + reference-SVG anchors — defined in
    /// <see cref="PdfToHtmlOpusExtractorPrompt"/> sibling file so the
    /// raw-string SVG content does not break the architecture-test
    /// scanner's single-quote-aware string-stripping. Surfaced as a
    /// public static here so tests can assert on its content.
    /// </summary>
    public static string SystemPrompt => PdfToHtmlOpusExtractorPrompt.SystemPrompt;

    private readonly ILogger<PdfToHtmlOpusExtractor> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDocumentStore _documentStore;
    private readonly IApiKeyCipher _cipher;
    private readonly IAnthropicLlmRuntime _runtime;
    private readonly IAnthropicPdfHtmlInvoker _invoker;
    private readonly IModelResolver? _modelResolver;
    private readonly IActivityPropagator? _activityPropagator;
    private readonly ILlmCostMetric? _featureCost;

    public PdfToHtmlOpusExtractor(
        ILogger<PdfToHtmlOpusExtractor> logger,
        IConfiguration configuration,
        IDocumentStore documentStore,
        IApiKeyCipher cipher,
        IAnthropicLlmRuntime runtime,
        IAnthropicPdfHtmlInvoker invoker,
        IModelResolver? modelResolver = null,
        IActivityPropagator? activityPropagator = null,
        ILlmCostMetric? featureCost = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(documentStore);
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(invoker);

        _logger = logger;
        _configuration = configuration;
        _documentStore = documentStore;
        _cipher = cipher;
        _runtime = runtime;
        _invoker = invoker;
        _modelResolver = modelResolver;
        _activityPropagator = activityPropagator;
        _featureCost = featureCost;
    }

    public async Task<PdfToHtmlResponse> ConvertAsync(
        PdfToHtmlRequest req, CancellationToken ct = default)
    {
        if (req is null) return Failure(null, "request is required.");
        if (req.PdfBytes is null || req.PdfBytes.Length == 0)
            return Failure(null, "pdfBytes is required.");
        if (string.IsNullOrWhiteSpace(req.PdfId))
            return Failure(null, "pdfId is required.");

        // Gate 1: API key. Without a key the LLM tier cannot run.
        var apiKey = await TryResolveApiKeyAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning(
                "PdfToHtmlOpusExtractor: no Anthropic API key configured (trace_id={TraceId} pdf={PdfId}) — render-html unavailable",
                SafeTraceId(), req.PdfId);
            return Failure(null,
                "No API key configured for Anthropic. Set Anthropic:ApiKey in Settings > AI Providers.");
        }

        // Resolve model id. Defaults to claude-opus-4-7 via routing-config;
        // the resolver honours per-task curator overrides on top.
        if (_modelResolver is null)
        {
            _logger.LogDebug(
                "PdfToHtmlOpusExtractor: no IModelResolver wired (test scaffolding) — refusing to call LLM (trace_id={TraceId} pdf={PdfId})",
                SafeTraceId(), req.PdfId);
            return Failure(null,
                "LLM render-html unavailable: IModelResolver not wired into this composition.");
        }
        string modelId;
        try
        {
            modelId = await _modelResolver.ResolveModelForTaskAsync(TaskName, ct).ConfigureAwait(false);
        }
        catch (ModelNotConfiguredException ex)
        {
            _logger.LogError(ex,
                "PdfToHtmlOpusExtractor: ModelResolver could not resolve task='{Task}' (trace_id={TraceId} pdf={PdfId})",
                TaskName, SafeTraceId(), req.PdfId);
            return Failure(null, ex.Message);
        }

        // Gate 2: per-model breaker.
        try { _runtime.RequestCircuitPermission(modelId); }
        catch (CircuitOpenException ex)
        {
            _logger.LogWarning(
                "PdfToHtmlOpusExtractor: circuit open (trace_id={TraceId} pdf={PdfId} model={Model} message={Message})",
                SafeTraceId(), req.PdfId, modelId, ex.Message);
            return Failure(modelId, ex.Message);
        }

        var instruction = string.IsNullOrWhiteSpace(req.Instruction)
            ? PdfToHtmlRequest.DefaultInstruction
            : req.Instruction!;

        var sw = Stopwatch.StartNew();
        var traceId = _activityPropagator?.GetTraceId();
        using var activity = _activityPropagator?.StartLlmActivity(TaskName);
        activity?.SetTag("trace_id", traceId);
        activity?.SetTag("task", TaskName);
        activity?.SetTag("tier", Tier);
        activity?.SetTag("model_id", modelId);
        activity?.SetTag("pdf_id", req.PdfId);
        activity?.SetTag("pdf_bytes", (long)req.PdfBytes.Length);

        try
        {
            var (text, inputTokens, outputTokens) = await _invoker.InvokeAsync(
                apiKey: apiKey!,
                modelId: modelId,
                systemPrompt: SystemPrompt,
                pdfBytes: req.PdfBytes,
                instruction: instruction,
                maxTokens: MaxHtmlTokens,
                ct: ct).ConfigureAwait(false);
            sw.Stop();

            // Cost is emitted on the success path regardless of whether the
            // model returned usable HTML — the round-trip cost real money.
            _runtime.EmitMetrics(modelId, TaskName, sw.ElapsedMilliseconds,
                inputTokens, outputTokens, AnthropicSupportedModels.ResolvePricingFor(modelId));
            _featureCost?.Record(
                feature: FeatureName,
                tier: Tier,
                task: TaskName,
                modelId: modelId,
                inputTokens: inputTokens,
                outputTokens: outputTokens);

            if (string.IsNullOrWhiteSpace(text))
            {
                activity?.SetTag("outcome", "empty");
                _logger.LogWarning(
                    "PdfToHtmlOpusExtractor: Anthropic returned empty text (trace_id={TraceId} pdf={PdfId} model={Model} duration_ms={DurationMs})",
                    traceId, req.PdfId, modelId, sw.ElapsedMilliseconds);
                // Empty response is an Anthropic-side anomaly, not a local
                // failure — don't trip the breaker. Mirrors OcrTextEnhancer.
                return Failure(modelId, "Anthropic returned an empty response.", inputTokens, outputTokens);
            }

            // Strip markdown fences if the model added them despite the
            // system-prompt rule. Some Opus turns emit ```html ... ``` even
            // when told not to, and stripping is cheap. We only strip when
            // the response opens with a fence; partial-fence garbage stays
            // visible so the curator can spot it on review.
            var html = StripHtmlFences(text!).Trim();

            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", inputTokens);
            activity?.SetTag("output_tokens", outputTokens);
            activity?.SetTag("html_chars", (long)html.Length);
            _logger.LogInformation(
                "PdfToHtmlOpusExtractor OK (trace_id={TraceId} pdf={PdfId} model={Model} duration_ms={DurationMs} input_tokens={InputTokens} output_tokens={OutputTokens} html_chars={HtmlChars})",
                traceId, req.PdfId, modelId, sw.ElapsedMilliseconds, inputTokens, outputTokens, html.Length);

            _runtime.RecordSuccess(modelId);
            return new PdfToHtmlResponse(
                Success: true,
                Html: html,
                ModelUsed: modelId,
                Error: null,
                InputTokens: inputTokens,
                OutputTokens: outputTokens);
        }
        catch (CircuitOpenException ex)
        {
            sw.Stop();
            // Race: breaker tripped between permission check and outbound
            // call. Already logged by the runtime; return failure cleanly.
            return Failure(modelId, ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            // Cancellation is upstream-driven (HTTP request abort). Don't
            // trip the breaker — the call may have succeeded server-side
            // and we just stopped waiting. Rethrow so the endpoint pipeline
            // can short-circuit. Mirrors LlmBagrutQuestionSegmenter.
            _logger.LogInformation(
                "PdfToHtmlOpusExtractor: cancellation requested (trace_id={TraceId} pdf={PdfId} duration_ms={DurationMs})",
                SafeTraceId(), req.PdfId, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _runtime.RecordFailure(modelId);
            activity?.SetTag("outcome", "error");
            _logger.LogError(ex,
                "PdfToHtmlOpusExtractor: Anthropic call failed (trace_id={TraceId} pdf={PdfId} model={Model} duration_ms={DurationMs})",
                SafeTraceId(), req.PdfId, modelId, sw.ElapsedMilliseconds);
            return Failure(modelId, ex.Message);
        }
    }

    // ── Internals ────────────────────────────────────────────────────────

    private static PdfToHtmlResponse Failure(
        string? modelId, string error, long input = 0, long output = 0)
        => new(
            Success: false,
            Html: string.Empty,
            ModelUsed: modelId,
            Error: error,
            InputTokens: input,
            OutputTokens: output);

    /// <summary>
    /// Strip a leading ```html / ```HTML fence and matching trailing ``` if
    /// the model emitted one despite the system-prompt rule. Defensive —
    /// the rule says "no markdown fences" but Opus turns occasionally add
    /// them anyway. Mid-response fences (the model used a code-fence inside
    /// the HTML for some reason) are left intact; we only strip wrap-style.
    /// </summary>
    internal static string StripHtmlFences(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        // Match ```html, ```HTML, ``` html, ```\n cases.
        const string fenceHtmlLower = "```html";
        const string fenceHtmlUpper = "```HTML";
        const string fenceBare = "```";

        int prefix = 0;
        if (trimmed.StartsWith(fenceHtmlLower, StringComparison.Ordinal))
            prefix = fenceHtmlLower.Length;
        else if (trimmed.StartsWith(fenceHtmlUpper, StringComparison.Ordinal))
            prefix = fenceHtmlUpper.Length;
        else if (trimmed.StartsWith(fenceBare, StringComparison.Ordinal))
            prefix = fenceBare.Length;

        if (prefix == 0) return text;

        // Strip the opening fence + one trailing newline (if present).
        var afterOpen = trimmed.Slice(prefix);
        if (afterOpen.Length > 0 && afterOpen[0] == '\n') afterOpen = afterOpen.Slice(1);
        else if (afterOpen.Length > 1 && afterOpen[0] == '\r' && afterOpen[1] == '\n') afterOpen = afterOpen.Slice(2);

        // Find the closing ``` (anchored to a newline-or-start) — to avoid
        // eating an inline ``` the model might have emitted as content, we
        // only strip when the closing fence appears at the END of the
        // (right-trimmed) string. That's the wrap-style shape the model
        // produces in practice.
        var afterOpenTrimmed = afterOpen.TrimEnd();
        if (afterOpenTrimmed.Length >= 3 &&
            afterOpenTrimmed[afterOpenTrimmed.Length - 3] == '`' &&
            afterOpenTrimmed[afterOpenTrimmed.Length - 2] == '`' &&
            afterOpenTrimmed[afterOpenTrimmed.Length - 1] == '`')
        {
            return afterOpenTrimmed.Slice(0, afterOpenTrimmed.Length - 3).TrimEnd().ToString();
        }

        // Opening fence with no matching close — unusual; return the
        // post-fence body so curators at least see HTML.
        return afterOpen.ToString();
    }

    private string SafeTraceId()
        => _activityPropagator?.GetTraceId() ?? "no-trace";

    /// <summary>
    /// Resolve the Anthropic API key — AiSettingsDocument first (curator-
    /// persisted, cipher-decrypted), then IConfiguration fallback. Mirrors
    /// LlmBagrutQuestionSegmenter exactly so a single AiSettings dialog
    /// covers every Anthropic call site.
    /// </summary>
    private async Task<string?> TryResolveApiKeyAsync(CancellationToken ct)
    {
        try
        {
            await using var session = _documentStore.QuerySession();
            var doc = await session.LoadAsync<AiSettingsDocument>(
                AiSettingsDocument.SingletonId, ct).ConfigureAwait(false);
            if (doc is not null && !string.IsNullOrEmpty(doc.AnthropicApiKeyCipher))
            {
                if (_cipher.TryDecryptFromWire(doc.AnthropicApiKeyCipher, out var plaintext))
                    return plaintext;

                _logger.LogError(
                    "[SIEM] PdfToHtmlOpusExtractor failed to decrypt persisted Anthropic API key — master key may have rotated, "
                    + "or the cipher blob is corrupt. Falling back to IConfiguration.");
            }
        }
        catch (Marten.Exceptions.MartenCommandException ex)
            when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
        {
            // First-run cold start: settings table not yet auto-created.
            _logger.LogDebug(
                "AiSettingsDocument table not yet created — using configuration fallback for PdfToHtmlOpusExtractor");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PdfToHtmlOpusExtractor: AiSettingsDocument lookup failed — falling back to IConfiguration");
        }

        var fromConfig = _configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }
}
