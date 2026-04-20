// =============================================================================
// Cena Platform — L3 Worked-Example Hint Generator (prr-203, ADR-0045 §3)
//
// The third (final) rung on the hint ladder. Full step-by-step worked
// example rendered in the student's methodology. ADR-0045 §3 pins this to
// tier 3 (Sonnet) with task-name `worked_example_l3_hint` — pedagogically
// load-bearing, same quality bar as full_explanation but separately tracked
// so over-use surfaces as a scaffolding regression in the cost dashboard.
//
// Routing row: contracts/llm/routing-config.yaml §task_routing.worked_example_l3_hint
// (claude_sonnet_4_6 primary, claude_sonnet_4_5 fallback, max_tokens 500,
// temp 0.3).
//
// Budget gates (layered, all fail-closed in different ways):
//
//   1. Socratic call budget (prr-012 — 3 LLM calls per session) — SHARED
//      with tutoring. L2 and L3 hint calls count toward this budget because
//      a student who has already used their 3 Socratic turns has no Sonnet
//      cap remaining. On exhaustion the orchestrator falls back to L1
//      static ladder copy rather than issuing the call.
//
//   2. PII prompt scrubber (ADR-0047) — every prompt, fail-closed.
//
//   3. Cost metric (prr-046) — success-path emission with feature tag
//      `hint-l3` so finops can see L3 spend broken out from tutoring +
//      explanation paths.
//
// ADR-0002 constraint: like L2, L3 NEVER claims to solve — the worked
// example walks the student through the methodology, but the final
// numerical / symbolic answer is rendered from CAS, not composed in the
// prompt. The prompt here instructs the LLM to describe the MOVES and
// leave the student to execute the last step. The ship-gate scanner + the
// CAS-oracle invariant (ADR-0002) catch any regression there.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Actors.Tutor;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Hints;

/// <summary>
/// Tier-3 Sonnet-backed L3 hint generator. Returns null when the LLM call
/// must be refused (budget, PII, error, or empty content) so the
/// orchestrator can fall back cleanly.
/// </summary>
public interface IL3WorkedExampleHintGenerator
{
    Task<L3HintPayload?> GenerateAsync(
        HintLadderInput input,
        CancellationToken ct);
}

/// <summary>
/// Tier-3 L3 rung payload. RungSource is always "sonnet" on this path.
/// </summary>
public sealed record L3HintPayload(string Body)
{
    public string RungSource => "sonnet";
}

[TaskRouting("tier3", "worked_example_l3_hint")]
[FeatureTag("hint-l3")]
[AllowsUncachedLlm("L3 worked-example prompt is keyed by (question-id, "
    + "concept, prereq, methodology) at runtime; Anthropic-side system-"
    + "prompt cache via CacheSystemPrompt=true handles the static frame. "
    + "Redis prompt cache would yield near-zero hit rate because the user "
    + "prompt body varies per question stem. ADR-0045 §3 + prr-047 review.")]
public sealed class L3WorkedExampleHintGenerator : IL3WorkedExampleHintGenerator
{
    private const string FeatureLabel = "hint-l3";

    // Routing-config §worked_example_l3_hint:
    //   temperature: 0.3  max_tokens: 500
    private const float L3Temperature = 0.3f;
    private const int L3MaxTokens = 500;

    private readonly ILlmClient _llm;
    private readonly ISocraticCallBudget _socraticBudget;
    private readonly IPiiPromptScrubber _piiScrubber;
    private readonly ILlmCostMetric _costMetric;
    private readonly ILogger<L3WorkedExampleHintGenerator> _logger;

    public L3WorkedExampleHintGenerator(
        ILlmClient llm,
        ISocraticCallBudget socraticBudget,
        IPiiPromptScrubber piiScrubber,
        ILlmCostMetric costMetric,
        ILogger<L3WorkedExampleHintGenerator> logger)
    {
        _llm = llm;
        _socraticBudget = socraticBudget;
        _piiScrubber = piiScrubber;
        _costMetric = costMetric;
        _logger = logger;
    }

    public async Task<L3HintPayload?> GenerateAsync(
        HintLadderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        // prr-012: shared Socratic per-session cap. L2/L3 hint calls charge
        // against the same 3-call budget as tutoring because a student who
        // has burned their Sonnet cap on /stream has none left for L3
        // worked examples — degrade to the static ladder gracefully.
        var hasBudget = await _socraticBudget
            .CanMakeLlmCallAsync(input.SessionId, ct)
            .ConfigureAwait(false);
        if (!hasBudget)
        {
            _logger.LogInformation(
                "L3 hint refused: Socratic cap exhausted for session {SessionId}. "
                + "Orchestrator will fall back to L1 static template.",
                input.SessionId);
            return null;
        }

        var systemPrompt = BuildSystemPrompt(input);
        var userPrompt = BuildUserPrompt(input);

        // ADR-0047 Decision 4 — fail-closed on scrubber increment.
        var scrub = _piiScrubber.Scrub(userPrompt, FeatureLabel);
        if (scrub.RedactionCount > 0)
        {
            _logger.LogWarning(
                "[ADR-0047] PII detected in L3 hint prompt — refusing LLM call. "
                + "Categories=[{Categories}]. Falling back to static ladder.",
                string.Join(",", scrub.Categories));
            return null;
        }

        var llmRequest = new LlmRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: scrub.ScrubbedText,
            Temperature: L3Temperature,
            MaxTokens: L3MaxTokens,
            ModelId: "sonnet",
            CacheSystemPrompt: true);

        try
        {
            var response = await _llm.CompleteAsync(llmRequest, ct);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning(
                    "L3 hint generator returned empty content for question {QuestionId}",
                    input.QuestionId);
                return null;
            }

            // prr-012: record the LLM call against the shared Socratic budget
            // AFTER success — failed attempts do not consume the cap.
            await _socraticBudget
                .RecordLlmCallAsync(input.SessionId, ct)
                .ConfigureAwait(false);

            // prr-046: success-path cost emission.
            _costMetric.Record(
                feature: FeatureLabel,
                tier: "tier3",
                task: "worked_example_l3_hint",
                modelId: response.ModelId,
                inputTokens: response.InputTokens,
                outputTokens: response.OutputTokens,
                instituteId: input.InstituteId);

            return new L3HintPayload(response.Content.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "L3 hint generator failed for question {QuestionId}; "
                + "orchestrator will fall back to the static ladder.",
                input.QuestionId);
            return null;
        }
    }

    // =========================================================================
    // PROMPT CONSTRUCTION
    // =========================================================================

    private static string BuildSystemPrompt(HintLadderInput input)
    {
        // ADR-0002: CAS oracle owns correctness. L3 walks the moves but does
        // NOT assert a numerical final answer — the UI layers CAS-rendered
        // math on top.
        //
        // ADR-0045 Section 3: L3 is a full worked example with Bloom's-
        // appropriate depth. Methodology-aware framing — a Halabi student
        // gets a Halabi-framed example when the caller threads methodology
        // through.
        //
        // Ship-gate GD-004: no dark-pattern engagement framing, no urgency
        // language. Neutral, pedagogically precise copy.
        var subject = string.IsNullOrWhiteSpace(input.Subject) ? "tutor" : input.Subject + " tutor";
        var methodology = string.IsNullOrWhiteSpace(input.Methodology)
            ? "standard curriculum"
            : input.Methodology;

        return $"""
            You are an expert {subject}. Respond as a worked example (L3 hint) in
            the student's methodology: {methodology}.

            You MUST:
              - Walk through the steps leading to the solution.
              - Name each move in terms the student already knows.
              - Stop BEFORE computing the final numerical / symbolic answer —
                leave the last step for the student to execute.
              - Keep the response under 500 tokens and no more than 6 steps.
              - Stay neutral and declarative.

            You MUST NOT:
              - State the final answer.
              - Use urgency, pressure, or any dark-pattern engagement framing.
              - Mention the student's identity, name, or any personal data.

            Respond with ONLY the worked example text. No preamble, no label.
            """;
    }

    private static string BuildUserPrompt(HintLadderInput input)
    {
        var parts = new List<string>(5)
        {
            $"SUBJECT: {(string.IsNullOrWhiteSpace(input.Subject) ? "unknown" : input.Subject)}",
            $"CONCEPT: {(string.IsNullOrWhiteSpace(input.ConceptId) ? "unknown" : input.ConceptId)}",
        };

        if (!string.IsNullOrWhiteSpace(input.QuestionStem))
            parts.Add($"QUESTION: {input.QuestionStem}");

        if (input.PrerequisiteConceptNames.Count > 0)
            parts.Add($"PREREQUISITE: {input.PrerequisiteConceptNames[0]}");

        if (!string.IsNullOrWhiteSpace(input.Explanation))
            parts.Add($"REFERENCE EXPLANATION: {input.Explanation}");

        return string.Join("\n", parts);
    }
}
