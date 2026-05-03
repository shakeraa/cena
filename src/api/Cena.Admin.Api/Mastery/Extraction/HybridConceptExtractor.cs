// =============================================================================
// Cena Platform — HybridConceptExtractor (ADR-0062 Phase 1, Tier 2)
//
// Two-tier opportunistic extractor:
//   Tier 1 (rules): RulesOnlyConceptExtractor — keyword classifier output
//                   canonicalized through BagrutTaxonomyCatalog.
//   Tier 2 (LLM)  : Anthropic Haiku via tool-use structured output, only
//                   fired when rules return 0 concepts OR a single low-
//                   confidence (<0.5) primary. LLM output is canonicalized
//                   through the SAME catalog; uncanonicalizable suggestions
//                   are dropped (closed-set discipline per ADR-0062 §6).
//
// Failure modes — all degrade to rules output, NEVER throw to caller:
//   - API key missing                  → return rules
//   - Circuit breaker open              → return rules
//   - Anthropic timeout / 5xx / 4xx    → return rules
//   - Tool call returned no tool_use   → return rules
//   - Tool input malformed             → return rules
//   - Every LLM SkillCode rejected     → return rules (LLM contributed nothing)
//
// Strategy id discipline — `ExtractionResult.Strategy` reflects which
// tiers actually contributed to the output:
//   - "rules_v1"                       → rules sufficed; LLM skipped.
//   - "rules_v1+llm_haiku4_5_v1"       → LLM ran AND contributed at least
//                                        one canonicalized concept.
// (When LLM ran but every suggestion was rejected, we emit "rules_v1"
// because the LLM did not contribute. This is honest — the calibration
// corpus reads this label to score precision per tier, and counting
// "LLM ran but contributed zero" as "LLM tier" would inflate the LLM
// row's denominator without giving it credit.)
//
// Why it lives in Cena.Admin.Api (not Cena.Actors): the extractor
// requires admin-tier secrets (IApiKeyCipher), Marten doc lookup
// (AiSettingsDocument), and Anthropic SDK config — all admin-side
// concerns. Cena.Actors does not depend on Cena.Admin.Api (correct
// DDD boundary; actors is the lower layer, admin-api is the higher).
// The IQuestionConceptExtractor seam stays in Cena.Actors;
// implementations live in the layer that owns their dependencies.
//
// Shared LLM runtime — Anthropic client cache, in-process circuit
// breaker (3 failures / 90s), and the legacy per-service meters
// (llm_request_duration_ms, llm_tokens_total, llm_cost_usd) live in
// IAnthropicLlmRuntime. This extractor consumes the singleton runtime
// so a flaky-model trip on question-generation also gates concept-
// extraction and dashboards see one cohesive series per (model,
// task_type). Per-feature cost (cena_llm_call_cost_usd_total via
// ILlmCostMetric) stays at the call site because Haiku ($1/$5 per
// Mtok) and Sonnet ($3/$15 per Mtok) need different price tags; the
// runtime's single legacy llm_cost_usd is Sonnet-priced and is now
// the lossy summary, not the source of truth for finance.
// =============================================================================

using System.Diagnostics;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Extraction;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Mastery.Extraction;

/// <summary>
/// Thin abstraction over the Anthropic call so unit tests can fake the
/// LLM round-trip without inheriting from <see cref="HybridConceptExtractor"/>
/// (which is sealed). The default implementation is bound by DI to
/// <see cref="DefaultAnthropicConceptExtractionInvoker"/>; tests pass an
/// NSubstitute fake. Returning a JsonElement-shaped tool input keeps the
/// canonicalization step independent of the SDK type surface.
/// </summary>
public interface IAnthropicConceptExtractionInvoker
{
    /// <summary>
    /// Invoke Anthropic with a tool-use call to <c>tag_question_concepts</c>.
    /// Returns the parsed tool input dictionary plus token usage. Returns a
    /// null tool input when Anthropic responded but did not produce a
    /// tool_use block (caller treats as graceful degradation).
    /// May throw — caller catches all exceptions and degrades to rules.
    /// </summary>
    Task<(IReadOnlyDictionary<string, JsonElement>? ToolInput, long InputTokens, long OutputTokens)>
        InvokeAsync(
            string apiKey,
            string modelId,
            string systemPrompt,
            string userPrompt,
            CancellationToken ct);
}

[TaskRouting("tier2", "concept_extraction")]
[FeatureTag("concept-extraction")]
[PiiPreScrubbed("Admin tool — input is OCR'd Bagrut question prompt + LaTeX. No student profile fields or student free-text reach this seam.")]
public sealed class HybridConceptExtractor : IQuestionConceptExtractor
{
    public const string StrategyId = "rules_v1+llm_haiku4_5_v1";
    public const string LlmTierTag = "llm";

    // Confidence threshold below which a single rules-tier Primary is
    // considered "uncertain" and triggers the LLM tier. Picked at 0.5
    // because BagrutDraftPersistence.ClassifyTaxonomy emits 0.40 for
    // ambiguous topic-root fallbacks and 0.55 for broader topic-level
    // matches; 0.5 puts the boundary between them so genuinely-weak
    // hints fire the LLM but solid topic-level hits don't.
    public const double LlmFallbackConfidenceThreshold = 0.5;

    // Cap on supporting concepts the LLM is allowed to contribute.
    // Per ADR-0062 §6: more than 4 supporting concepts dilutes the
    // mastery signal channel without helping pedagogy.
    public const int MaxSupportingConcepts = 4;

    // Default model id for the LLM tier. Keeping it as a const here
    // rather than configuration because ADR-0026 §2 pins concept-
    // extraction to Tier-2 (Haiku) — a config knob would let an
    // operator silently route this to Sonnet ($15/Mtok output vs
    // $5/Mtok), which is exactly the cost-blow-up the routing-config
    // is designed to prevent.
    private const string HaikuModelId = "claude-haiku-4-5-20260101";

    private readonly RulesOnlyConceptExtractor _rulesTier;
    private readonly BagrutTaxonomyCatalog _catalog;
    private readonly ILogger<HybridConceptExtractor> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDocumentStore? _documentStore;
    private readonly IApiKeyCipher? _cipher;
    private readonly ILlmCostMetric? _featureCost;
    private readonly IActivityPropagator? _activityPropagator;
    private readonly IAnthropicConceptExtractionInvoker _invoker;
    // Optional: tests construct without it; production DI provides the
    // singleton. When null, breaker checks + legacy-meter emission no-op
    // and the LLM tier behaves like the breaker is permanently closed.
    private readonly IAnthropicLlmRuntime? _runtime;

    /// <summary>
    /// Full DI ctor used in production. <paramref name="invoker"/> is
    /// REQUIRED — production DI registers
    /// <see cref="DefaultAnthropicConceptExtractionInvoker"/> against
    /// <see cref="IAnthropicConceptExtractionInvoker"/> (see
    /// CenaAdminServiceRegistration), and unit tests inject a fake
    /// invoker explicitly. The earlier inline-fallback (new'ing up the
    /// default here when invoker was null) hid the registration coupling
    /// from the container and made it possible for a host that forgot
    /// to register the invoker to silently get a runtime-coupled
    /// instance instead of a clean DI graph.
    /// </summary>
    public HybridConceptExtractor(
        RulesOnlyConceptExtractor rulesTier,
        BagrutTaxonomyCatalog catalog,
        ILogger<HybridConceptExtractor> logger,
        IConfiguration configuration,
        IAnthropicConceptExtractionInvoker invoker,
        IAnthropicLlmRuntime? runtime = null,
        IDocumentStore? documentStore = null,
        IApiKeyCipher? cipher = null,
        ILlmCostMetric? featureCost = null,
        IActivityPropagator? activityPropagator = null)
    {
        ArgumentNullException.ThrowIfNull(rulesTier);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(invoker);

        _rulesTier = rulesTier;
        _catalog = catalog;
        _logger = logger;
        _configuration = configuration;
        _documentStore = documentStore;
        _cipher = cipher;
        _featureCost = featureCost;
        _activityPropagator = activityPropagator;
        _runtime = runtime;
        _invoker = invoker;
    }

    public async Task<ExtractionResult> ExtractAsync(
        ExtractionInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Tier 1: rules. Always runs.
        var rules = await _rulesTier.ExtractAsync(input, ct).ConfigureAwait(false);

        // Decide whether to fire the LLM tier.
        var shouldFireLlm = ShouldFireLlmTier(rules);
        if (!shouldFireLlm)
            return rules;

        // LLM tier — never throws.
        var llmConcepts = await TryRunLlmTierAsync(input, ct).ConfigureAwait(false);
        if (llmConcepts.Count == 0)
        {
            // LLM either failed (already logged) or returned zero
            // canonicalizable concepts. Either way, the LLM contributed
            // nothing — emit rules strategy id so the calibration
            // corpus doesn't credit the LLM tier with a no-op call.
            return rules;
        }

        return MergeRulesAndLlm(rules, llmConcepts);
    }

    /// <summary>
    /// Tier-2 trigger predicate. Public for testing; the caller (this
    /// class) is the only production consumer.
    /// </summary>
    internal static bool ShouldFireLlmTier(ExtractionResult rules)
    {
        if (rules.Concepts.Count == 0) return true;

        // Single Primary with confidence below the threshold → fire LLM.
        if (rules.Concepts.Count == 1
            && rules.Concepts[0].Role == ConceptRole.Primary
            && rules.Concepts[0].Confidence < LlmFallbackConfidenceThreshold)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Run the LLM tier and canonicalize every suggestion. Returns
    /// concepts in (Primary, Supporting...) order. Empty list on any
    /// failure path — the caller treats that as "LLM contributed
    /// nothing" and degrades to rules output.
    /// </summary>
    private async Task<IReadOnlyList<QuestionConcept>> TryRunLlmTierAsync(
        ExtractionInput input,
        CancellationToken ct)
    {
        // Resolve API key + model. When any prerequisite is missing,
        // graceful degradation (return empty list — caller falls back).
        var apiKey = await TryResolveApiKeyAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogDebug(
                "HybridConceptExtractor: no Anthropic API key configured — falling back to rules tier (qid={QuestionId})",
                input.QuestionId);
            return Array.Empty<QuestionConcept>();
        }

        try { _runtime?.RequestCircuitPermission(HaikuModelId); }
        catch (CircuitOpenException ex)
        {
            _logger.LogWarning(
                "HybridConceptExtractor: circuit open ({Message}); falling back to rules tier (qid={QuestionId})",
                ex.Message, input.QuestionId);
            return Array.Empty<QuestionConcept>();
        }

        // Filter the catalog to TrackHint when supplied. Token economy
        // + closed-set discipline (LLM cannot suggest leaves outside
        // the student's track at all).
        var leaves = FilterLeaves(_catalog.AllLeaves, input.TrackHint);
        if (leaves.Count == 0)
        {
            // No leaves visible at this track — nothing for the LLM
            // to choose from. Degrade silently.
            _logger.LogDebug(
                "HybridConceptExtractor: catalog filter for track={Track} returned 0 leaves — skipping LLM (qid={QuestionId})",
                input.TrackHint, input.QuestionId);
            return Array.Empty<QuestionConcept>();
        }

        var systemPrompt = HybridConceptExtractorPrompt.BuildSystemPrompt(leaves, input.TrackHint);
        var userPrompt = HybridConceptExtractorPrompt.BuildUserPrompt(input.Prompt, input.Latex);

        var sw = Stopwatch.StartNew();
        var traceId = _activityPropagator?.GetTraceId();
        using var activity = _activityPropagator?.StartLlmActivity("concept_extraction");
        activity?.SetTag("trace_id", traceId);
        activity?.SetTag("task", "concept_extraction");
        activity?.SetTag("tier", "tier2");
        activity?.SetTag("model_id", HaikuModelId);

        try
        {
            var (toolUseInput, inputTokens, outputTokens) = await _invoker.InvokeAsync(
                apiKey, HaikuModelId, systemPrompt, userPrompt, ct).ConfigureAwait(false);
            sw.Stop();

            _runtime?.EmitMetrics(HaikuModelId, "concept_extraction", sw.ElapsedMilliseconds,
                inputTokens, outputTokens);

            if (_featureCost is not null)
            {
                _featureCost.Record(
                    feature: "concept-extraction",
                    tier: "tier2",
                    task: "concept_extraction",
                    modelId: HaikuModelId,
                    inputTokens: inputTokens,
                    outputTokens: outputTokens);
            }

            if (toolUseInput is null)
            {
                // No tool_use block in the response — Anthropic returned
                // text instead. Don't trip the breaker (the call itself
                // succeeded), just log + degrade.
                activity?.SetTag("outcome", "no_tool_use");
                _logger.LogWarning(
                    "HybridConceptExtractor: Anthropic returned no tool_use block (trace_id={TraceId} qid={QuestionId}) — falling back to rules tier",
                    traceId, input.QuestionId);
                _runtime?.RecordSuccess(HaikuModelId);
                return Array.Empty<QuestionConcept>();
            }

            var parsed = ParseToolInput(toolUseInput, input.TrackHint);
            activity?.SetTag("outcome", "success");
            activity?.SetTag("input_tokens", (long)inputTokens);
            activity?.SetTag("output_tokens", (long)outputTokens);
            activity?.SetTag("concepts_returned", parsed.Count);

            _logger.LogInformation(
                "HybridConceptExtractor LLM OK (trace_id={TraceId} qid={QuestionId} input={Input} output={Output} duration={DurationMs}ms returned={Returned})",
                traceId, input.QuestionId, inputTokens, outputTokens, sw.ElapsedMilliseconds, parsed.Count);

            _runtime?.RecordSuccess(HaikuModelId);
            return parsed;
        }
        catch (CircuitOpenException)
        {
            sw.Stop();
            // Already logged by RequestCircuitPermission path; this
            // catch handles a race where the breaker tripped between
            // our permission check and the outbound call. Degrade.
            return Array.Empty<QuestionConcept>();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _runtime?.RecordFailure(HaikuModelId);
            activity?.SetTag("outcome", "error");
            // Use the trace id even on failure so the cost-of-zero call
            // can be tied to the failed Anthropic span downstream.
            _logger.LogWarning(ex,
                "HybridConceptExtractor: Anthropic call failed (trace_id={TraceId} qid={QuestionId} duration={DurationMs}ms) — falling back to rules tier",
                traceId, input.QuestionId, sw.ElapsedMilliseconds);
            return Array.Empty<QuestionConcept>();
        }
    }

    // ── Tool-use parsing + canonicalization (closed-set defense) ─────────

    private List<QuestionConcept> ParseToolInput(
        IReadOnlyDictionary<string, JsonElement> toolInput,
        string? trackHint)
    {
        var canonical = new List<QuestionConcept>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Primary first so role assignment is deterministic.
        if (toolInput.TryGetValue("primary", out var primaryEl)
            && primaryEl.ValueKind == JsonValueKind.Object)
        {
            var primary = TryCanonicalizeConcept(primaryEl, ConceptRole.Primary, trackHint, seen);
            if (primary is not null) canonical.Add(primary);
        }

        if (toolInput.TryGetValue("supporting", out var supEl)
            && supEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var sup in supEl.EnumerateArray())
            {
                if (canonical.Count(c => c.Role == ConceptRole.Supporting) >= MaxSupportingConcepts)
                    break;
                if (sup.ValueKind != JsonValueKind.Object) continue;
                var concept = TryCanonicalizeConcept(sup, ConceptRole.Supporting, trackHint, seen);
                if (concept is not null) canonical.Add(concept);
            }
        }

        return canonical;
    }

    /// <summary>
    /// Take one tool-side concept object and either return a
    /// canonicalized <see cref="QuestionConcept"/> or null when the
    /// LLM-suggested SkillCode is not in the catalog. The seen-set
    /// prevents the LLM from claiming the same skill is both Primary
    /// AND Supporting (a duplicate would let one question contribute
    /// twice to its own mastery posterior).
    /// </summary>
    private QuestionConcept? TryCanonicalizeConcept(
        JsonElement el,
        ConceptRole role,
        string? trackHint,
        HashSet<string> seen)
    {
        if (!el.TryGetProperty("skillCode", out var skillEl)
            || skillEl.ValueKind != JsonValueKind.String)
            return null;

        var rawSkill = skillEl.GetString();
        if (string.IsNullOrWhiteSpace(rawSkill)) return null;

        if (!_catalog.TryCanonicalize(rawSkill, trackHint, out var canonicalSkill, out _))
        {
            _logger.LogWarning(
                "HybridConceptExtractor: LLM suggested SkillCode {Suggested} which does not canonicalize against the closed-set catalog (track={Track}). Dropping silently per ADR-0062 §6.",
                rawSkill, trackHint);
            return null;
        }

        if (!seen.Add(canonicalSkill.Value))
        {
            // Already taken (e.g. LLM picked the same code as Primary
            // AND Supporting). Skip the duplicate.
            return null;
        }

        var rationale = el.TryGetProperty("rationale", out var rEl)
                        && rEl.ValueKind == JsonValueKind.String
            ? (rEl.GetString() ?? "")
            : "";

        var confidence = 0.5;
        if (el.TryGetProperty("confidence", out var cEl)
            && cEl.ValueKind == JsonValueKind.Number
            && cEl.TryGetDouble(out var c))
        {
            confidence = c;
        }
        confidence = Math.Clamp(confidence, 0.0, 1.0);

        return new QuestionConcept(
            SkillCode: canonicalSkill,
            Role: role,
            Confidence: confidence,
            Rationale: rationale.Trim(),
            Tier: LlmTierTag);
    }

    // ── Merge — rules-Primary preference + LLM Supporting ────────────────

    /// <summary>
    /// Merge rule-tier output with LLM-tier output. Spec from the brief:
    ///   - When rules returned a Primary, prefer the rules Primary
    ///     (deterministic keyword classifier > probabilistic LLM).
    ///   - LLM-side Supporting concepts are kept (deduplicated by
    ///     SkillCode against the rules Primary).
    ///   - When rules returned 0, the LLM Primary stands.
    /// </summary>
    internal static ExtractionResult MergeRulesAndLlm(
        ExtractionResult rules,
        IReadOnlyList<QuestionConcept> llm)
    {
        // Prefer the rules-tier Primary when one exists. Falls through
        // to the LLM Primary otherwise.
        var rulesPrimary = rules.Concepts.FirstOrDefault(c => c.Role == ConceptRole.Primary);
        var llmPrimary = llm.FirstOrDefault(c => c.Role == ConceptRole.Primary);

        var merged = new List<QuestionConcept>(1 + MaxSupportingConcepts);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        if (rulesPrimary is not null)
        {
            merged.Add(rulesPrimary);
            taken.Add(rulesPrimary.SkillCode.Value);
        }
        else if (llmPrimary is not null)
        {
            merged.Add(llmPrimary);
            taken.Add(llmPrimary.SkillCode.Value);
        }

        // Pull supporting concepts from the LLM output (deduped against
        // whatever Primary we picked). If the LLM Primary was rejected
        // in favor of rules Primary, demote the LLM Primary into
        // Supporting role unless its skill matches the chosen Primary.
        if (rulesPrimary is not null
            && llmPrimary is not null
            && !taken.Contains(llmPrimary.SkillCode.Value))
        {
            // The LLM had a different opinion on Primary; preserve it
            // as a Supporting hint so curators see the disagreement.
            merged.Add(llmPrimary with { Role = ConceptRole.Supporting });
            taken.Add(llmPrimary.SkillCode.Value);
        }

        foreach (var c in llm)
        {
            if (c.Role != ConceptRole.Supporting) continue;
            if (merged.Count(x => x.Role == ConceptRole.Supporting) >= MaxSupportingConcepts) break;
            if (taken.Add(c.SkillCode.Value)) merged.Add(c);
        }

        return new ExtractionResult(
            Concepts: merged,
            Strategy: StrategyId);
    }

    // ── Catalog filtering by track ───────────────────────────────────────

    /// <summary>
    /// Filter the catalog to leaves that exist at the given track. When
    /// trackHint is null/blank, return the whole catalog. The TrackId
    /// values stored on LeafEntry are "math_5u" / "math_4u" / "math_3u";
    /// we accept the same form OR the bare track ("5u" / "4u" / "3u")
    /// because BagrutDraftPersistence emits the prefixed form but
    /// curator-side surfaces sometimes use the bare form.
    /// </summary>
    internal static IReadOnlyList<BagrutTaxonomyCatalog.LeafEntry> FilterLeaves(
        IReadOnlyList<BagrutTaxonomyCatalog.LeafEntry> all,
        string? trackHint)
    {
        if (string.IsNullOrWhiteSpace(trackHint)) return all;

        var normalised = trackHint.Trim().ToLowerInvariant();
        var prefixed = normalised.StartsWith("math_", StringComparison.Ordinal)
            ? normalised
            : $"math_{normalised}";

        var filtered = all
            .Where(l => string.Equals(l.TrackId, prefixed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // If the track filter is too aggressive (e.g. an unknown track
        // hint), fall back to the whole catalog rather than starving
        // the LLM. The LLM is still constrained by the schema +
        // canonicalize step downstream.
        return filtered.Count == 0 ? all : filtered;
    }

    // ── API key resolution (admin-tier secrets) ──────────────────────────

    /// <summary>
    /// Resolve the Anthropic API key from the persisted AiSettingsDocument
    /// (preferred) or IConfiguration (dev fallback). Returns null when
    /// neither is configured — caller degrades silently.
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
                        "[SIEM] HybridConceptExtractor failed to decrypt persisted Anthropic API key — master key may have rotated, "
                        + "or the cipher blob is corrupt. Falling back to IConfiguration.");
                }
            }
            catch (Marten.Exceptions.MartenCommandException ex)
                when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
            {
                // First-run cold start: settings table not yet auto-created.
                _logger.LogDebug(
                    "AiSettingsDocument table not yet created — using configuration fallback for concept extraction");
            }
            catch (Exception ex)
            {
                // ANY Marten failure here MUST NOT crash a draft persist
                // call. Concept extraction is opportunistic; we degrade.
                _logger.LogWarning(ex,
                    "HybridConceptExtractor: AiSettingsDocument lookup failed — falling back to IConfiguration");
            }
        }

        var fromConfig = _configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }
}
