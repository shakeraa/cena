// =============================================================================
// Cena Platform — L2 Haiku Ideation Hint Generator (prr-203, ADR-0045 §3)
//
// The second rung on the hint ladder. Short structured "here's the method"
// suggestion (<150 tokens) conditioned on the question stem + concept. ADR-
// 0045 §3 pins this to tier 2 (Haiku) with task-name `ideation_l2_hint` —
// bounded output shape, no multi-step reasoning, cheap ($0.0002/call
// ceiling).
//
// Routing row: contracts/llm/routing-config.yaml §task_routing.ideation_l2_hint
// (claude_haiku_4_5 primary, kimi_k2_0905 fallback, max_tokens 150, temp 0.2).
//
// Rules (enforced by tests + scanners):
//   - Carries [TaskRouting("tier2", "ideation_l2_hint")].
//   - Carries [FeatureTag("hint-l2")] for the finops cost-center split
//     (prr-046); over-use of L2 is a scaffolding signal, separately
//     trackable from L3.
//   - Passes every user prompt through IPiiPromptScrubber (ADR-0047) and
//     fails closed — if the scrubber reports RedactionCount > 0 we return
//     null, the orchestrator falls back to L1 static template, and the
//     scrubber's severity-1 counter alerts on-call.
//   - Emits ILlmCostMetric.Record on success (prr-046).
//   - Never claims to solve — ADR-0002 boundary: the CAS is the correctness
//     oracle; L2 hint copy only names a method, it does not execute it.
//     Prompt instruction below enforces this in-band.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Hints;

/// <summary>
/// Tier-2 Haiku-backed L2 hint generator. Returns null when the LLM call
/// must be refused (PII, error, or empty content) so the orchestrator can
/// cleanly fall back to the L1 static template without claiming L2.
/// </summary>
public interface IL2HaikuHintGenerator
{
    Task<L2HintPayload?> GenerateAsync(
        HintLadderInput input,
        CancellationToken ct);
}

/// <summary>
/// Tier-2 L2 rung payload. RungSource is always "haiku" on this path.
/// </summary>
public sealed record L2HintPayload(string Body)
{
    public string RungSource => "haiku";
}

[TaskRouting("tier2", "ideation_l2_hint")]
[FeatureTag("hint-l2")]
[AllowsUncachedLlm("L2 hint prompt is keyed by (question-id, concept, prereq) "
    + "at runtime; Anthropic-side system-prompt cache via CacheSystemPrompt=true "
    + "handles the static frame. The user-prompt body varies per question and "
    + "carries no repeatable high-token suffix — a Redis prompt cache would "
    + "yield near-zero hit rate. ADR-0045 §3 + prr-047 review.")]
public sealed class L2HaikuHintGenerator : IL2HaikuHintGenerator
{
    private const string FeatureLabel = "hint-l2";
    private const string HaikuModelId = "claude-haiku-4-5-20260101";

    // Routing-config §ideation_l2_hint:
    //   temperature: 0.2  max_tokens: 150
    // Locked here to make the cost ceiling explicit to the reader.
    private const float L2Temperature = 0.2f;
    private const int L2MaxTokens = 150;

    private readonly ILlmClient _llm;
    private readonly IPiiPromptScrubber _piiScrubber;
    private readonly ILlmCostMetric _costMetric;
    private readonly IActivityPropagator _activityPropagator;
    private readonly ILogger<L2HaikuHintGenerator> _logger;

    public L2HaikuHintGenerator(
        ILlmClient llm,
        IPiiPromptScrubber piiScrubber,
        ILlmCostMetric costMetric,
        IActivityPropagator activityPropagator,
        ILogger<L2HaikuHintGenerator> logger)
    {
        _llm = llm;
        _piiScrubber = piiScrubber;
        _costMetric = costMetric;
        _activityPropagator = activityPropagator;
        _logger = logger;
    }

    public async Task<L2HintPayload?> GenerateAsync(
        HintLadderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var systemPrompt = BuildSystemPrompt(input);
        var userPrompt = BuildUserPrompt(input);

        // ADR-0047 Decision 4 — fail-closed. If the scrubber finds anything,
        // we refuse the LLM call; the orchestrator degrades to L1 static
        // template. The scrubber itself emits the sev-1 counter increment.
        var scrub = _piiScrubber.Scrub(userPrompt, FeatureLabel);
        if (scrub.RedactionCount > 0)
        {
            _logger.LogWarning(
                "[ADR-0047] PII detected in L2 hint prompt — refusing LLM call. "
                + "Categories=[{Categories}]. Falling back to L1 template.",
                string.Join(",", scrub.Categories));
            return null;
        }

        var llmRequest = new LlmRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: scrub.ScrubbedText,
            Temperature: L2Temperature,
            MaxTokens: L2MaxTokens,
            ModelId: HaikuModelId,
            CacheSystemPrompt: true);

        // prr-143: trace-id on every LLM call attempt. Stamped here rather
        // than inside the ILlmClient adapter so the call-site scanner can
        // see a visible GetTraceId reference in this file.
        var traceId = _activityPropagator.GetTraceId();
        using var activity = _activityPropagator.StartLlmActivity("ideation_l2_hint");
        activity?.SetTag("trace_id", traceId);
        activity?.SetTag("task", "ideation_l2_hint");
        activity?.SetTag("tier", "tier2");
        activity?.SetTag("question_id", input.QuestionId);

        try
        {
            var response = await _llm.CompleteAsync(llmRequest, ct);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning(
                    "L2 hint generator returned empty content (trace_id={TraceId} question={QuestionId})",
                    traceId, input.QuestionId);
                return null;
            }

            // prr-046: success-path cost emission.
            _costMetric.Record(
                feature: FeatureLabel,
                tier: "tier2",
                task: "ideation_l2_hint",
                modelId: response.ModelId,
                inputTokens: response.InputTokens,
                outputTokens: response.OutputTokens,
                instituteId: input.InstituteId);

            activity?.SetTag("outcome", "success");
            _logger.LogInformation(
                "L2 hint OK (trace_id={TraceId} question={QuestionId} input={Input} output={Output})",
                traceId, input.QuestionId, response.InputTokens, response.OutputTokens);

            return new L2HintPayload(response.Content.Trim());
        }
        catch (OperationCanceledException)
        {
            // Propagate cooperative cancellation verbatim so the request
            // pipeline can tear down cleanly. No metric, no log noise.
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetTag("outcome", "error");
            _logger.LogWarning(ex,
                "L2 hint generator failed (trace_id={TraceId} question={QuestionId}); "
                + "orchestrator will fall back to L1 template.",
                traceId, input.QuestionId);
            return null;
        }
    }

    // =========================================================================
    // PROMPT CONSTRUCTION
    // =========================================================================

    private static string BuildSystemPrompt(HintLadderInput input)
    {
        // ADR-0002 boundary: the CAS is the sole correctness oracle. L2 hints
        // NEVER execute the solution — they name a method the student should
        // try. The prompt makes this invariant explicit in-band.
        //
        // ADR-0045 Section 3: L2 is "here's the method" — short (<150 tokens),
        // concrete action verb + object, no worked example.
        //
        // Ship-gate GD-004: neutral, declarative copy. No dark-pattern
        // engagement mechanics; no urgency framing. The tone is "pick a
        // technique, try the first line" — matches Cena's house style.
        return $"""
            You are an expert {(string.IsNullOrWhiteSpace(input.Subject) ? "tutor" : input.Subject + " tutor")}.
            You produce METHOD HINTS (L2) — you name a single technique the student should try.

            You MUST NOT:
              - Solve the problem.
              - Show the final answer.
              - Reveal numerical or symbolic values from the solution.
              - Use urgency, pressure, or any dark-pattern engagement framing.

            You MUST:
              - Name exactly one method or technique (e.g. "factor the common term",
                "isolate the variable", "balance the equation first").
              - Tell the student which part of the problem to apply it to.
              - Keep the response under 150 tokens and under 3 sentences.
              - Stay neutral and declarative.

            Respond with ONLY the hint text. No preamble, no label, no markdown headers.
            """;
    }

    private static string BuildUserPrompt(HintLadderInput input)
    {
        var parts = new List<string>(4)
        {
            $"SUBJECT: {(string.IsNullOrWhiteSpace(input.Subject) ? "unknown" : input.Subject)}",
            $"CONCEPT: {(string.IsNullOrWhiteSpace(input.ConceptId) ? "unknown" : input.ConceptId)}",
        };

        if (!string.IsNullOrWhiteSpace(input.QuestionStem))
            parts.Add($"QUESTION: {input.QuestionStem}");

        if (input.PrerequisiteConceptNames.Count > 0)
            parts.Add($"PREREQUISITE: {input.PrerequisiteConceptNames[0]}");

        return string.Join("\n", parts);
    }
}
