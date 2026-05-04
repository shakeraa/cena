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
//
// Vision-aware enhance (2026-05-04 — t_3401e9e79877)
// --------------------------------------------------
// User-reported defect on corpus/tests/35581-q.pdf page 2 Q1: source
// `1/4 v² + 13/6 v + 10/3` was reconstructed as `10/3 v² + 13/4 v + 1/2`
// — the LLM hallucinated when reading Poppler's two-line stacked-
// fraction layout from text alone. The fix passes the SOURCE PAGE PNG
// to Sonnet 4.6 as an ImageBlockParam so the model can use the visual
// layout as ground truth. Wiring discipline:
//   - request DTO carries optional SourcePdfId + SourcePage; older drafts
//     leave both null and the enhancer behaves identically to the
//     pre-vision implementation (text-only invoker path).
//   - PNG resolution is cache-first: PdfPageRasterizer writes its output
//     under {SourcePageStorageOptions.RootDirectory}/{pdfId}/page-{N:D3}.png.
//     We try D3 then D2 then D1 padding to match the rasterizer's
//     historical filename shapes.
//   - On cache miss, we on-demand rasterize from the persisted PDF bytes
//     (IBagrutPdfStore) so a curator who clicks Enhance on an item where
//     the rasterizer hasn't run yet still gets the vision pass.
//   - On any failure (rasterizer absent, store absent, IO error,
//     pdftoppm missing) we drop to the legacy text-only invoker path
//     and log a WARN. Fail-open is the right move: the pre-vision text-
//     only enhance is still useful, and the rebuilt prompt at b55839f3
//     gives the LLM a reasonable shot even without the image.
//   - Cache key incorporates the PNG bytes hash when an image is
//     attached, so a curator who re-uploads a different version of the
//     same exam doesn't get a stale cached text-only result.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Cena.Admin.Api.AiSettings;
using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion;

// ── ADR-0062 Phase 1.5 — OCR cleanup pass DTOs ──
//
// SourcePdfId + SourcePage are OPTIONAL. When supplied, the enhancer
// attempts to attach the rendered page PNG to the LLM call as an image
// block (ADR-0062 Phase 1.5 vision-aware extension). Older drafts ingested
// before SourcePdfId/SourcePage were captured leave both null; behaviour
// in that path is identical to the pre-vision implementation.
public sealed record EnhanceOcrTextRequest(
    string OcrText,
    string? SourceContext = null,
    string? SourcePdfId = null,
    int? SourcePage = null);

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
    /// <summary>
    /// Canonical task name routed through <see cref="IModelResolver"/>.
    /// Matches contracts/llm/routing-config.yaml § default_model_by_task:.
    /// </summary>
    public const string TaskName = "ocr_text_enhance";

    private readonly ILogger<OcrTextEnhancer> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDocumentStore _documentStore;
    private readonly IApiKeyCipher _cipher;
    private readonly ILlmCostMetric _featureCost;
    private readonly IOcrEnhancementCache _cache;
    private readonly IAnthropicLlmRuntime _runtime;
    private readonly IAnthropicEnhanceInvoker _invoker;
    private readonly IModelResolver? _modelResolver;
    private readonly IActivityPropagator? _activityPropagator;
    // Vision-aware enhance — all three are OPTIONAL because:
    //   - the rasterizer / PDF store are vision-extractor-branch wiring
    //     that may be absent in a unit-test composition,
    //   - the storage options are bound from configuration and may not be
    //     wired in a minimal test scaffold.
    // Any null collapses the vision attempt to the legacy text-only path.
    private readonly IPdfPageRasterizer? _rasterizer;
    private readonly IBagrutPdfStore? _pdfStore;
    private readonly SourcePageStorageOptions? _pageStorageOptions;

    /// <summary>
    /// Last-resort model id used ONLY when no <see cref="IModelResolver"/>
    // Removed FallbackSonnetModelId const (gap-1 cleanup, 2026-05-03):
    // when no IModelResolver is wired (pure unit-test path), the LLM tier
    // refuses to call rather than substituting a hardcoded id. The resolver
    // IS the seam, and tests that bypass it intentionally exercise the
    // "no LLM available" path. Same shape as HybridConceptExtractor.

    // Cache observability — separated from token counters so finops
    // dashboards see "$ saved by cache hit" distinct from "$ spent on
    // LLM call". Tagged by task_type so this meter shape can host other
    // task types in the future (currently just ocr_text_enhance).
    // Kept here because it's OCR-enhance-specific; the legacy per-service
    // triple lives in IAnthropicLlmRuntime now.
    private readonly Counter<long> _cacheHits;

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
        IAnthropicEnhanceInvoker invoker,
        IActivityPropagator? activityPropagator = null,
        IModelResolver? modelResolver = null,
        IPdfPageRasterizer? rasterizer = null,
        IBagrutPdfStore? pdfStore = null,
        IOptions<SourcePageStorageOptions>? pageStorageOptions = null)
    {
        _logger = logger;
        _configuration = configuration;
        _documentStore = documentStore;
        _cipher = cipher;
        _featureCost = featureCost;
        _cache = cache;
        _runtime = runtime;
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _modelResolver = modelResolver;
        _activityPropagator = activityPropagator;
        _rasterizer = rasterizer;
        _pdfStore = pdfStore;
        _pageStorageOptions = pageStorageOptions?.Value;

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

        // Resolve the source-page PNG up-front (best-effort). When this
        // fails we fall through to the legacy text-only invoker shape; the
        // cache key MUST reflect whether an image was attached so a stale
        // text-only cached result doesn't serve a vision-eligible request.
        ReadOnlyMemory<byte>? pagePng = null;
        if (!string.IsNullOrWhiteSpace(req.SourcePdfId)
            && req.SourcePage is int page && page >= 1)
        {
            pagePng = await TryResolvePagePngAsync(req.SourcePdfId!, page, ct)
                .ConfigureAwait(false);
        }

        // Cache lookup (sha256-keyed). Hits skip API-key resolution +
        // circuit breaker + LLM call entirely. The cache key is
        // surfaced on the response so the endpoint can persist it on
        // the draft alongside the enhanced text.
        //
        // Key composition: when an image is attached, fold the PNG bytes
        // into the hash so a curator re-uploading a different version of
        // the same exam doesn't get a stale text-only result. When no
        // image is attached, the key stays exactly sha256(text) — which
        // preserves cache hits on every existing row written before this
        // change.
        var inputHash = ComputeCompositeKey(req.OcrText, pagePng);
        var cached = await _cache.TryGetAsync(inputHash, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _cacheHits.Add(1,
                new KeyValuePair<string, object?>("task_type", "ocr_text_enhance"),
                new KeyValuePair<string, object?>("model_id", cached.ModelUsed));
            _logger.LogInformation(
                "OcrTextEnhancer cache HIT (model={Model} computedAt={ComputedAt} hash={Hash} hasImage={HasImage})",
                cached.ModelUsed, cached.ComputedAt, inputHash, pagePng is not null);
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

        // Resolve the per-task model id via ModelResolver — curator override
        // first, then routing-config default. NOTE: pre-resolver behaviour
        // honoured doc.AnthropicModelId directly; that field is now the
        // /api/admin/ai/settings PUT seam (general settings dropdown), while
        // the per-task override panel writes to ModelOverridesByTask. The
        // resolver returns the override if present, else falls through to
        // the routing-config default for ocr_text_enhance (Sonnet 4.6).
        // doc.AnthropicModelId is intentionally NOT consulted here because
        // it is the "default model for ad-hoc question generation" knob,
        // not the OCR-enhance-specific knob.
        if (_modelResolver is null)
        {
            _logger.LogDebug(
                "OcrTextEnhancer: no IModelResolver wired (test scaffolding) — skipping LLM enhance");
            return new EnhanceOcrTextResponse(false, "", null,
                "LLM enhance unavailable: IModelResolver not wired into this composition.");
        }
        string modelName;
        try
        {
            modelName = await _modelResolver.ResolveModelForTaskAsync(TaskName, ct).ConfigureAwait(false);
        }
        catch (ModelNotConfiguredException ex)
        {
            _logger.LogError(ex,
                "OcrTextEnhancer: ModelResolver could not resolve task='{Task}'", TaskName);
            return new EnhanceOcrTextResponse(false, "", null, ex.Message);
        }

        try { _runtime.RequestCircuitPermission(modelName); }
        catch (CircuitOpenException ex)
        {
            return new EnhanceOcrTextResponse(false, "", modelName, ex.Message);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var systemPrompt =
                "You are an OCR-cleanup assistant for Israeli Bagrut math papers. " +
                "Input: text extracted from a Bagrut question via Poppler `pdftotext -layout` " +
                "(Hebrew/Arabic prose interleaved with math). " +
                "Output rules: " +
                "1) Wrap inline math in \\( ... \\) and display math in \\[ ... \\]. " +
                "2) Preserve Hebrew/Arabic prose verbatim and keep RTL paragraph flow. " +
                "3) Restore paragraph breaks; do not invent content. " +
                "4) Where the source clearly references a figure (e.g. 'see graph', 'in the diagram'), " +
                "insert a marker on its own line: [[FIGURE:p<page>]] when a page is given, else [[FIGURE]]. " +
                "5) Return ONLY the cleaned text. No commentary. " +
                "" +
                "FRACTION RECONSTRUCTION (load-bearing): Poppler emits stacked fractions " +
                "as TWO consecutive lines — numerators on the upper line, denominators " +
                "(plus the algebraic expression) on the lower line, aligned by column. " +
                "When you see a line of bare numerators directly above a line containing " +
                "an algebraic expression, pair each numerator with the FIRST digit at its " +
                "approximate column position on the lower line and emit \\frac{n}{d} for " +
                "each pair. Read columns LEFT-TO-RIGHT in display order even though the " +
                "surrounding prose is RTL — fractions are LTR. Example: " +
                "\n  Input lines: '   1     13     10\\n  4 v 2 + 6 v + 3'" +
                "\n  Correct output: \\(\\frac{1}{4}v^2 + \\frac{13}{6}v + \\frac{10}{3}\\)" +
                "\n  WRONG output (column drift): \\(\\frac{10}{3}v^2 + \\frac{13}{4}v + \\frac{1}{2}\\) " +
                "" +
                "EXPONENT RECONSTRUCTION: a digit (typically 2 or 3) appearing immediately " +
                "after a variable letter (v, x, y, t, etc.) and separated from it by a single " +
                "space is the variable's EXPONENT — emit v^2 not 'v 2 +'. The lone digit is " +
                "NOT a free term; it's a superscript that the layout-mode pdftotext flattens " +
                "to inline.";

            var traceId = _activityPropagator?.GetTraceId();
            using var activity = _activityPropagator?.StartLlmActivity("ocr_text_enhance");
            activity?.SetTag("trace_id", traceId);
            activity?.SetTag("task", "ocr_text_enhance");
            activity?.SetTag("tier", "tier3");
            activity?.SetTag("model_id", modelName);
            activity?.SetTag("has_image", pagePng is not null);

            var (text, inputTokens, outputTokens) = await _invoker.InvokeAsync(
                apiKey: apiKey!,
                modelId: modelName,
                systemPrompt: systemPrompt,
                ocrText: req.OcrText,
                sourcePagePng: pagePng,
                maxTokens: MaxEnhanceTokens,
                ct: ct).ConfigureAwait(false);
            sw.Stop();

            // Pricing per-call. The resolver may have returned Sonnet (default)
            // or any curator-overridden model; AnthropicSupportedModels maps the
            // chosen id back to its $/Mtok rates so the legacy meter stays
            // accurate after a Sonnet→Haiku flip on the override panel.
            _runtime.EmitMetrics(modelName, "ocr_text_enhance", sw.ElapsedMilliseconds,
                inputTokens, outputTokens, AnthropicSupportedModels.ResolvePricingFor(modelName));

            // prr-046: canonical per-feature cost counter (cena_llm_call_cost_usd_total)
            _featureCost.Record(
                feature: "ocr-text-enhance",
                tier: "tier3",
                task: "ocr_text_enhance",
                modelId: modelName,
                inputTokens: inputTokens,
                outputTokens: outputTokens);

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
            activity?.SetTag("input_tokens", inputTokens);
            activity?.SetTag("output_tokens", outputTokens);
            _logger.LogInformation(
                "OcrTextEnhancer OK (trace_id={TraceId} model={Model} input={Input} output={Output} duration={DurationMs}ms hash={Hash} hasImage={HasImage})",
                traceId, modelName, inputTokens, outputTokens, sw.ElapsedMilliseconds, inputHash, pagePng is not null);

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

    // ── Source-page PNG resolution ────────────────────────────────────────

    /// <summary>
    /// Best-effort resolve the page PNG bytes for a (pdfId, pageNumber) pair.
    /// Fail-open: returns null on every error path so the caller drops to
    /// the legacy text-only invoker shape. Logs at WARN for visibility but
    /// does not propagate.
    ///
    /// Resolution order:
    ///   1. Cache lookup under <see cref="SourcePageStorageOptions.RootDirectory"/>
    ///      using the same SafeId / D3-D2-D1 padding fallback the visual-
    ///      review GET endpoint already accepts (keeps shape parity).
    ///   2. On miss, if both <see cref="IPdfPageRasterizer"/> and
    ///      <see cref="IBagrutPdfStore"/> are wired, re-rasterize the
    ///      whole PDF on demand (idempotent — the rasterizer no-ops when
    ///      the destination directory already has files), then re-read
    ///      the requested page.
    /// </summary>
    private async Task<ReadOnlyMemory<byte>?> TryResolvePagePngAsync(
        string sourcePdfId, int pageNumber, CancellationToken ct)
    {
        // Cache lookup needs the storage root.
        if (_pageStorageOptions is null)
        {
            _logger.LogDebug(
                "OcrTextEnhancer: SourcePageStorageOptions not wired — skipping vision PNG attach (pdf={PdfId} page={Page})",
                sourcePdfId, pageNumber);
            return null;
        }

        var safeId = SafeIdSegment(sourcePdfId);
        var pageDir = Path.Combine(_pageStorageOptions.RootDirectory, safeId);
        var cachedPath = TryFindPagePngOnDisk(pageDir, pageNumber);
        if (cachedPath is not null)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(cachedPath, ct).ConfigureAwait(false);
                return bytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OcrTextEnhancer: cache PNG read failed (pdf={PdfId} page={Page} path={Path}) — falling through to on-demand rasterize",
                    sourcePdfId, pageNumber, cachedPath);
            }
        }

        // Cache miss (or unreadable cache file) — try on-demand rasterize.
        if (_rasterizer is null || _pdfStore is null)
        {
            _logger.LogDebug(
                "OcrTextEnhancer: rasterizer or pdfStore unwired — skipping vision PNG attach (pdf={PdfId} page={Page})",
                sourcePdfId, pageNumber);
            return null;
        }

        try
        {
            await using var pdfStream = await _pdfStore.OpenReadAsync(sourcePdfId, ct)
                .ConfigureAwait(false);
            if (pdfStream is null)
            {
                _logger.LogDebug(
                    "OcrTextEnhancer: pdfStore has no bytes for pdf={PdfId} (backfilled item) — skipping vision PNG attach",
                    sourcePdfId);
                return null;
            }
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, ct).ConfigureAwait(false);
            var pdfBytes = ms.ToArray();

            await _rasterizer.RasterizeAsync(pdfBytes, sourcePdfId, ct).ConfigureAwait(false);

            var resolvedPath = TryFindPagePngOnDisk(pageDir, pageNumber);
            if (resolvedPath is null)
            {
                _logger.LogWarning(
                    "OcrTextEnhancer: rasterize succeeded but page {Page} not found under {Dir} — skipping vision PNG attach (pdf={PdfId})",
                    pageNumber, pageDir, sourcePdfId);
                return null;
            }
            var bytes = await File.ReadAllBytesAsync(resolvedPath, ct).ConfigureAwait(false);
            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OcrTextEnhancer: on-demand rasterize failed (pdf={PdfId} page={Page}) — falling back to text-only enhance",
                sourcePdfId, pageNumber);
            return null;
        }
    }

    /// <summary>
    /// Look for page-{N:D3}.png, page-{N:D2}.png, page-{N}.png in
    /// <paramref name="pageDir"/>. Mirrors the resolver shape in
    /// VisualReviewEndpoints so cache hits agree on filename.
    /// </summary>
    private static string? TryFindPagePngOnDisk(string pageDir, int pageNumber)
    {
        if (string.IsNullOrEmpty(pageDir) || !Directory.Exists(pageDir))
            return null;

        // Sandbox the resolved path: it must stay under pageDir. The pdfId
        // is sanitised by SafeIdSegment, but defence-in-depth here matches
        // the same check the GET /page/N.png endpoint applies.
        var rootFull = Path.GetFullPath(pageDir);
        foreach (var fileName in new[]
        {
            $"page-{pageNumber:D3}.png",
            $"page-{pageNumber:D2}.png",
            $"page-{pageNumber}.png",
        })
        {
            var candidate = Path.GetFullPath(Path.Combine(pageDir, fileName));
            if (!candidate.StartsWith(
                    rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Strict whitelist for path-segment use of pdfId. Mirrors
    /// PdfPageRasterizer.SafeId / VisualReviewEndpoints.SafeIdSegment
    /// so the rasteriser's directory and the enhancer's lookup agree.
    /// </summary>
    private static string SafeIdSegment(string id)
    {
        var span = id.AsSpan();
        var buf = new char[span.Length];
        int j = 0;
        foreach (var ch in span)
        {
            var ok = (ch >= '0' && ch <= '9')
                  || (ch >= 'a' && ch <= 'z')
                  || (ch >= 'A' && ch <= 'Z')
                  || ch == '-' || ch == '_';
            if (ok) buf[j++] = ch;
        }
        return j == 0 ? "_" : new string(buf, 0, j).ToLowerInvariant();
    }

    /// <summary>
    /// Compose the cache key. When no PNG is attached, returns the same
    /// sha256(text) the cache used before this change (preserves every
    /// existing row). When a PNG IS attached, folds sha256(png) into the
    /// key so a re-uploaded variant of the same exam doesn't get a stale
    /// text-only cached result.
    /// </summary>
    private string ComputeCompositeKey(string ocrText, ReadOnlyMemory<byte>? pagePng)
    {
        if (pagePng is null)
            return _cache.ComputeKey(ocrText);

        // Prefix with a tag so the keyspace cannot collide with the legacy
        // text-only key. The tag is part of the hash input, not the hash
        // output, so the resulting key is still a 64-char lower-hex sha256.
        Span<byte> textBytesStack = stackalloc byte[0];
        var textBytes = Encoding.UTF8.GetBytes(ocrText);
        var pngBytes = pagePng.Value.ToArray();

        using var sha = SHA256.Create();
        sha.TransformBlock(Encoding.ASCII.GetBytes("v1:img:"), 0, 7, null, 0);
        sha.TransformBlock(textBytes, 0, textBytes.Length, null, 0);
        sha.TransformBlock(Encoding.ASCII.GetBytes("|"), 0, 1, null, 0);
        sha.TransformFinalBlock(pngBytes, 0, pngBytes.Length);
        var hash = sha.Hash!;
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    // Pricing resolution moved to AnthropicSupportedModels.ResolvePricingFor
    // (single source of truth for the closed-set model→rates mapping).
}
