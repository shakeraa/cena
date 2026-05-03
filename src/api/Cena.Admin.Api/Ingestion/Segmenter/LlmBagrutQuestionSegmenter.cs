// =============================================================================
// Cena Platform — LlmBagrutQuestionSegmenter (ADR-0062, ADR-0026 Tier 2)
//
// Primary impl of IBagrutQuestionSegmenter. Calls Anthropic Haiku once per
// PDF with the full OCR-page text, parses the tool-use response, validates
// the segments against the actual page list, and emits the result.
//
// Failure modes — every one of these falls back to OneDraftPerPageSegmenter
// after a single WARN log line carrying trace_id + pdfId + duration:
//   - feature flag OFF                   → never call Anthropic
//   - API key missing                    → fall back
//   - circuit breaker open (per model)   → fall back (already logged by runtime)
//   - SDK throws (timeout, 5xx, 4xx)     → fall back, breaker recorded as failure
//   - Anthropic returned no tool_use     → fall back, breaker recorded as success
//                                          (the call itself succeeded; just no
//                                          structured output)
//   - tool input is malformed JSON       → fall back
//   - segments empty AND PDF non-empty   → fall back (one-draft-per-page is the
//                                          safer default than emitting zero
//                                          drafts)
//   - segment references a page not in   → fall back (faulty segmenter; the
//     the OCR result                       caller cannot trust any of its picks)
//
// Cost metric is emitted ONLY on the success path that produced a usable
// segment list. A call that succeeded but produced no usable output still
// records the success branch for the breaker (Anthropic was reachable),
// AND records the cost on the per-feature counter (the call cost real
// money — finops dashboards must see it).
//
// trace_id is stamped on every log line via IActivityPropagator. The
// IActivityPropagator+GetTraceId tokens are also load-bearing for the
// ADR-0026 architecture test (EveryLlmServiceEmitsTraceIdTest scans for
// these token strings on every [TaskRouting] file).
// =============================================================================

using System.Diagnostics;
using System.Text.Json;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Cena.Infrastructure.Security;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion.Segmenter;

[TaskRouting("tier2", "bagrut_segmentation")]
[FeatureTag("content-segmentation")]
[PiiPreScrubbed("Admin tool — input is OCR'd Bagrut question text + LaTeX. Bagrut PDFs are Ministry-published exams; no student profile fields, no student free-text reach this seam.")]
public sealed class LlmBagrutQuestionSegmenter : IBagrutQuestionSegmenter
{
    /// <summary>
    /// IConfiguration key for the feature flag that gates this segmenter.
    /// Default OFF — the curator validates LLM-tier output on a few real
    /// Bagrut PDFs before flipping to ON in dev/staging/prod.
    /// </summary>
    public const string EnabledFlagKey = "Cena:Ingestion:BagrutLlmSegmenterEnabled";

    /// <summary>
    /// Last-resort model id used ONLY when no <see cref="IModelResolver"/>
    // Removed FallbackHaikuModelId const (gap-1 cleanup, 2026-05-03):
    // when no IModelResolver is wired (pure unit-test path), the segmenter
    // refuses to call the LLM and falls back to OneDraftPerPageSegmenter
    // rather than substituting a hardcoded model id. Same shape as the
    // HybridConceptExtractor + OcrTextEnhancer cleanup. Tests that want
    // the LLM tier wire a fake IModelResolver; tests that want the
    // legacy per-page output skip the resolver entirely.

    private const string FeatureName = "content-segmentation";
    private const string TaskName = "bagrut_segmentation";
    private const string Tier = "tier2";

    private readonly OneDraftPerPageSegmenter _fallback;
    private readonly IAnthropicSegmenterInvoker _invoker;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmBagrutQuestionSegmenter> _logger;

    private readonly IAnthropicLlmRuntime? _runtime;
    private readonly IDocumentStore? _documentStore;
    private readonly IApiKeyCipher? _cipher;
    private readonly ILlmCostMetric? _featureCost;
    private readonly IActivityPropagator? _activityPropagator;
    private readonly IModelResolver? _modelResolver;

    public LlmBagrutQuestionSegmenter(
        OneDraftPerPageSegmenter fallback,
        IAnthropicSegmenterInvoker invoker,
        IConfiguration configuration,
        ILogger<LlmBagrutQuestionSegmenter> logger,
        IAnthropicLlmRuntime? runtime = null,
        IDocumentStore? documentStore = null,
        IApiKeyCipher? cipher = null,
        ILlmCostMetric? featureCost = null,
        IActivityPropagator? activityPropagator = null,
        IModelResolver? modelResolver = null)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _fallback = fallback;
        _invoker = invoker;
        _configuration = configuration;
        _logger = logger;
        _runtime = runtime;
        _documentStore = documentStore;
        _cipher = cipher;
        _featureCost = featureCost;
        _activityPropagator = activityPropagator;
        _modelResolver = modelResolver;
    }

    public async Task<IReadOnlyList<BagrutQuestionSegment>> SegmentAsync(
        IReadOnlyList<ExtractedPage> pages,
        string examCode,
        string pdfId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(examCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfId);

        // Gate 0: feature flag. When OFF the LLM tier is invisible —
        // ingestion behaviour is identical to the pre-flag pipeline.
        if (!IsFlagEnabled())
        {
            return _fallback.Segment(pages);
        }

        // Gate 1: nothing to segment. Defer to the fallback for an empty
        // page list so callers don't pay a Haiku round-trip on a triage
        // edge-case (encrypted PDF, scan-only PDF that the cascade
        // returned empty).
        if (pages.Count == 0)
        {
            return _fallback.Segment(pages);
        }

        // Gate 2: API key. Without a key the LLM tier cannot run; degrade
        // silently to the fallback. Mirrors HybridConceptExtractor.
        var apiKey = await TryResolveApiKeyAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogDebug(
                "LlmBagrutQuestionSegmenter: no Anthropic API key configured (trace_id={TraceId} pdf={PdfId}) — falling back to one-draft-per-page",
                SafeTraceId(), pdfId);
            return _fallback.Segment(pages);
        }

        // Resolve model id via ModelResolver (curator override or
        // routing-config default — Haiku for bagrut_segmentation). When
        // no resolver is wired (pure unit-test path) the LLM tier
        // refuses to call and we fall back to one-draft-per-page —
        // resolver IS the seam, no hardcoded model-id substitute.
        if (_modelResolver is null)
        {
            _logger.LogDebug(
                "LlmBagrutQuestionSegmenter: no IModelResolver wired (test scaffolding) — falling back to one-draft-per-page (trace_id={TraceId} pdf={PdfId})",
                SafeTraceId(), pdfId);
            return _fallback.Segment(pages);
        }
        string modelId;
        try
        {
            modelId = await _modelResolver.ResolveModelForTaskAsync(TaskName, ct).ConfigureAwait(false);
        }
        catch (ModelNotConfiguredException ex)
        {
            _logger.LogError(ex,
                "LlmBagrutQuestionSegmenter: ModelResolver could not resolve task='{Task}' (trace_id={TraceId} pdf={PdfId}) — falling back to one-draft-per-page",
                TaskName, SafeTraceId(), pdfId);
            return _fallback.Segment(pages);
        }

        // Gate 3: per-model breaker. A Haiku trip from concept-extraction
        // also gates this caller (intentional — both routes use the same
        // model on the same Anthropic account; a flaky-model trip should
        // suppress further outbound traffic for the open window).
        try { _runtime?.RequestCircuitPermission(modelId); }
        catch (CircuitOpenException ex)
        {
            _logger.LogWarning(
                "LlmBagrutQuestionSegmenter: circuit open (trace_id={TraceId} pdf={PdfId} message={Message}) — falling back to one-draft-per-page",
                SafeTraceId(), pdfId, ex.Message);
            return _fallback.Segment(pages);
        }

        var systemPrompt = LlmBagrutQuestionSegmenterPrompt.SystemPrompt;
        var userPrompt = LlmBagrutQuestionSegmenterPrompt.BuildUserPrompt(
            examCode: examCode, pdfId: pdfId, pages: pages);

        var sw = Stopwatch.StartNew();
        var traceId = _activityPropagator?.GetTraceId();
        using var activity = _activityPropagator?.StartLlmActivity(TaskName);
        activity?.SetTag("trace_id", traceId);
        activity?.SetTag("task", TaskName);
        activity?.SetTag("tier", Tier);
        activity?.SetTag("model_id", modelId);
        activity?.SetTag("pdf_id", pdfId);
        activity?.SetTag("page_count", (long)pages.Count);

        try
        {
            var (toolInput, inputTokens, outputTokens) = await _invoker.InvokeAsync(
                apiKey, modelId, systemPrompt, userPrompt, ct).ConfigureAwait(false);
            sw.Stop();

            // Cost is emitted on the success path regardless of whether the
            // tool produced usable output — the round-trip cost real money.
            // Pricing tracks ModelResolver — a Haiku→Sonnet override flips
            // the rate from $1/$5 per Mtok to $3/$15 per Mtok automatically.
            _runtime?.EmitMetrics(
                modelId, TaskName, sw.ElapsedMilliseconds,
                inputTokens, outputTokens, AnthropicSupportedModels.ResolvePricingFor(modelId));
            _featureCost?.Record(
                feature: FeatureName,
                tier: Tier,
                task: TaskName,
                modelId: modelId,
                inputTokens: inputTokens,
                outputTokens: outputTokens);

            if (toolInput is null)
            {
                activity?.SetTag("outcome", "no_tool_use");
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: Anthropic returned no tool_use block (trace_id={TraceId} pdf={PdfId} duration_ms={DurationMs} input_tokens={InputTokens} output_tokens={OutputTokens}) — falling back to one-draft-per-page",
                    traceId, pdfId, sw.ElapsedMilliseconds, inputTokens, outputTokens);
                _runtime?.RecordSuccess(modelId);
                return _fallback.Segment(pages);
            }

            var parsed = TryParseSegments(toolInput, pages, traceId, pdfId);
            if (parsed is null)
            {
                // Already logged by TryParseSegments at WARN.
                activity?.SetTag("outcome", "validation_failed");
                _runtime?.RecordSuccess(modelId);
                return _fallback.Segment(pages);
            }

            // Empty segments + non-empty PDF is suspicious — the prompt is
            // explicit that empty is only valid for instructions-only PDFs.
            // The user-reported defect (35581-q.pdf) had 6 OCR pages with
            // 4 real questions; an LLM that returned 0 segments would be a
            // worse outcome than the per-page heuristic. Fall back so the
            // curator at least sees drafts.
            if (parsed.Count == 0 && AnyPagePopulated(pages))
            {
                activity?.SetTag("outcome", "empty_segments_non_empty_pdf");
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: LLM returned 0 segments for a non-empty PDF (trace_id={TraceId} pdf={PdfId} duration_ms={DurationMs} pages={PageCount}) — falling back to one-draft-per-page",
                    traceId, pdfId, sw.ElapsedMilliseconds, pages.Count);
                _runtime?.RecordSuccess(modelId);
                return _fallback.Segment(pages);
            }

            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", inputTokens);
            activity?.SetTag("output_tokens", outputTokens);
            activity?.SetTag("segments_returned", (long)parsed.Count);
            _logger.LogInformation(
                "LlmBagrutQuestionSegmenter OK (trace_id={TraceId} pdf={PdfId} duration_ms={DurationMs} input_tokens={InputTokens} output_tokens={OutputTokens} pages={PageCount} segments={SegmentCount})",
                traceId, pdfId, sw.ElapsedMilliseconds, inputTokens, outputTokens, pages.Count, parsed.Count);
            _runtime?.RecordSuccess(modelId);
            return parsed;
        }
        catch (CircuitOpenException)
        {
            sw.Stop();
            // Race: breaker tripped between permission check and outbound
            // call. Already-logged by the runtime; degrade.
            return _fallback.Segment(pages);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            // Cancellation is upstream-driven (request abort). Don't trip
            // the breaker — the call may have succeeded server-side and we
            // just stopped waiting. Log at INFO for trace stitching, then
            // rethrow so the caller (BagrutPdfIngestionService) can surface
            // the cancellation up the stack.
            _logger.LogInformation(
                "LlmBagrutQuestionSegmenter: cancellation requested (trace_id={TraceId} pdf={PdfId} duration_ms={DurationMs})",
                SafeTraceId(), pdfId, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _runtime?.RecordFailure(modelId);
            activity?.SetTag("outcome", "error");
            _logger.LogWarning(ex,
                "LlmBagrutQuestionSegmenter: Anthropic call failed (trace_id={TraceId} pdf={PdfId} duration_ms={DurationMs}) — falling back to one-draft-per-page",
                SafeTraceId(), pdfId, sw.ElapsedMilliseconds);
            return _fallback.Segment(pages);
        }
    }

    // ── Internals ────────────────────────────────────────────────────────

    private bool IsFlagEnabled()
    {
        var raw = _configuration[EnabledFlagKey];
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    /// <summary>
    /// Resolve the Anthropic API key from the persisted AiSettingsDocument
    /// (preferred) or IConfiguration (dev fallback). Returns null when
    /// neither is configured. Mirrors HybridConceptExtractor exactly so a
    /// single-source-of-truth admin AiSettings dialog covers both call sites.
    /// </summary>
    private async Task<string?> TryResolveApiKeyAsync(CancellationToken ct)
    {
        if (_documentStore is not null && _cipher is not null)
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
                        "[SIEM] LlmBagrutQuestionSegmenter failed to decrypt persisted Anthropic API key — master key may have rotated, "
                        + "or the cipher blob is corrupt. Falling back to IConfiguration.");
                }
            }
            catch (Marten.Exceptions.MartenCommandException ex)
                when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
            {
                // First-run cold start: settings table not yet auto-created.
                _logger.LogDebug(
                    "AiSettingsDocument table not yet created — using configuration fallback for Bagrut segmenter");
            }
            catch (Exception ex)
            {
                // ANY Marten failure here MUST NOT crash a PDF ingestion
                // call. Segmentation is opportunistic; we degrade.
                _logger.LogWarning(ex,
                    "LlmBagrutQuestionSegmenter: AiSettingsDocument lookup failed — falling back to IConfiguration");
            }
        }

        var fromConfig = _configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }

    /// <summary>
    /// Project the tool_use input dictionary into a validated list of
    /// <see cref="BagrutQuestionSegment"/>. Returns null when the response is
    /// malformed (caller falls back). Validates that every page number
    /// referenced by the LLM exists in the OCR page list — a hallucinated
    /// page index is a contract violation that disqualifies the entire
    /// response.
    /// </summary>
    private IReadOnlyList<BagrutQuestionSegment>? TryParseSegments(
        IReadOnlyDictionary<string, JsonElement> toolInput,
        IReadOnlyList<ExtractedPage> pages,
        string? traceId,
        string pdfId)
    {
        if (!toolInput.TryGetValue("segments", out var segmentsEl)
            || segmentsEl.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning(
                "LlmBagrutQuestionSegmenter: tool input missing/invalid 'segments' array (trace_id={TraceId} pdf={PdfId}) — falling back",
                traceId, pdfId);
            return null;
        }

        var validPages = new HashSet<int>();
        foreach (var page in pages) validPages.Add(page.PageNumber);

        var result = new List<BagrutQuestionSegment>();
        foreach (var seg in segmentsEl.EnumerateArray())
        {
            if (seg.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: non-object segment entry (trace_id={TraceId} pdf={PdfId}) — falling back",
                    traceId, pdfId);
                return null;
            }

            if (!seg.TryGetProperty("start_page", out var startEl)
                || !startEl.TryGetInt32(out var startPage))
            {
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: segment missing/invalid start_page (trace_id={TraceId} pdf={PdfId}) — falling back",
                    traceId, pdfId);
                return null;
            }
            if (!seg.TryGetProperty("end_page", out var endEl)
                || !endEl.TryGetInt32(out var endPage))
            {
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: segment missing/invalid end_page (trace_id={TraceId} pdf={PdfId}) — falling back",
                    traceId, pdfId);
                return null;
            }

            if (endPage < startPage)
            {
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: segment end_page<{Start} for start_page={End} (trace_id={TraceId} pdf={PdfId}) — falling back",
                    startPage, endPage, traceId, pdfId);
                return null;
            }

            // Every page in [startPage, endPage] must exist in the OCR list.
            for (var p = startPage; p <= endPage; p++)
            {
                if (!validPages.Contains(p))
                {
                    _logger.LogWarning(
                        "LlmBagrutQuestionSegmenter: segment references page {Page} which is not in the OCR result (trace_id={TraceId} pdf={PdfId} valid_pages=[{ValidPages}]) — falling back",
                        p, traceId, pdfId, string.Join(",", validPages));
                    return null;
                }
            }

            string? label = null;
            if (seg.TryGetProperty("question_label_or_null", out var labelEl)
                && labelEl.ValueKind == JsonValueKind.String)
            {
                var raw = labelEl.GetString();
                label = string.IsNullOrWhiteSpace(raw) ? null : raw!.Trim();
            }

            var confidence = 0.5;
            if (seg.TryGetProperty("confidence", out var confEl)
                && confEl.ValueKind == JsonValueKind.Number
                && confEl.TryGetDouble(out var c))
            {
                confidence = c;
            }
            confidence = Math.Clamp(confidence, 0.0, 1.0);

            var segment = new BagrutQuestionSegment(
                StartPage: startPage,
                EndPage: endPage,
                QuestionLabel: label,
                Confidence: confidence);

            // Defense in depth: if the segment is somehow invalid after
            // construction (shouldn't happen given the checks above) the
            // record's Validate throws — surface as a fall-back path.
            try { segment.Validate(); }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex,
                    "LlmBagrutQuestionSegmenter: post-parse segment validation failed (trace_id={TraceId} pdf={PdfId}) — falling back",
                    traceId, pdfId);
                return null;
            }

            result.Add(segment);
        }

        // Reject overlapping ranges. Sort by start_page; if any segment
        // starts at-or-before the previous segment's end_page, the LLM
        // produced an inconsistent slicing — fall back rather than emit
        // duplicate-page drafts.
        result.Sort(static (a, b) => a.StartPage.CompareTo(b.StartPage));
        for (var i = 1; i < result.Count; i++)
        {
            if (result[i].StartPage <= result[i - 1].EndPage)
            {
                _logger.LogWarning(
                    "LlmBagrutQuestionSegmenter: overlapping segments [{A}-{B}] vs [{C}-{D}] (trace_id={TraceId} pdf={PdfId}) — falling back",
                    result[i - 1].StartPage, result[i - 1].EndPage,
                    result[i].StartPage, result[i].EndPage,
                    traceId, pdfId);
                return null;
            }
        }

        return result;
    }

    private static bool AnyPagePopulated(IReadOnlyList<ExtractedPage> pages)
    {
        foreach (var page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.RawText)
                || !string.IsNullOrWhiteSpace(page.ExtractedLatex))
            {
                return true;
            }
        }
        return false;
    }

    private string SafeTraceId()
    {
        try { return _activityPropagator?.GetTraceId() ?? "no-trace"; }
        catch { return "no-trace"; }
    }
}
