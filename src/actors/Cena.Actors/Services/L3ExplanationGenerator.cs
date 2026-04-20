// =============================================================================
// Cena Platform -- L3 Personalized Explanation Generator (SAI-004)
//
// Extends L2 generation with full student context:
//   - Affect-aware gates (skip if ConfusionResolving or Bored_TooEasy)
//   - Verbosity scaling by FocusLevel
//   - Behavioral signal acknowledgment (backspace, answer changes)
//   - Language: Hebrew/Arabic based on question language
//   - Methodology constraint in system prompt
//
// L3 fires ONLY when L2 cache misses. On success, the result is cached
// back to L2 for future hits when the error type is classifiable.
//
// PRIVACY: No student ID, name, or PII is ever included in prompts.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Generates personalized explanations using full student context (L3).
/// Returns null when affect gates suppress generation.
/// </summary>
public interface IL3ExplanationGenerator
{
    /// <summary>
    /// Generate a personalized explanation using full student context.
    /// Returns null if affect gates determine generation should be skipped
    /// (e.g., student is in productive confusion or material is too easy).
    /// </summary>
    Task<GeneratedExplanation?> GenerateAsync(L3ExplanationRequest request, CancellationToken ct);
}

// ADR-0045: Personalised explanation via Sonnet with methodology+affect+scaffolding
// shaping the prompt. Pedagogically load-bearing — tier-3. Routing row:
// contracts/llm/routing-config.yaml §task_routing.full_explanation.
//
// prr-047: L3 fires only on L2 cache MISS, and the outer orchestrator owns
// both the L2 read (IExplanationCacheService.GetAsync) and the write-back on
// success. Inlining the cache here would double-dip the same Redis key. The
// CacheSystemPrompt=true flag on the LlmRequest still activates Anthropic's
// provider-side system-prompt cache per routing-config §6.system_prompt.
// prr-046: finops cost-center "explanation-l3". Shares the `full_explanation`
// routing row with ExplanationGenerator (L2 fallback), but bills separately
// so finops can see if L3 usage drifts upward — over-use of L3 signals a
// scaffolding regression (ADR-0045 §3 rationale).
[TaskRouting("tier3", "full_explanation")]
[FeatureTag("explanation-l3")]
[AllowsUncachedLlm("L3 fires only on L2 cache miss; caller (ExplanationOrchestrator) owns the cache read/write cycle. System prompt itself is cached via Anthropic cache_control.")]
public sealed class L3ExplanationGenerator : IL3ExplanationGenerator
{
    private readonly ILlmClient _llm;
    private readonly ILogger<L3ExplanationGenerator> _logger;
    private readonly ILlmCostMetric _costMetric;

    public L3ExplanationGenerator(
        ILlmClient llm,
        ILogger<L3ExplanationGenerator> logger,
        ILlmCostMetric costMetric)
    {
        _llm = llm;
        _logger = logger;
        _costMetric = costMetric;
    }

    public async Task<GeneratedExplanation?> GenerateAsync(
        L3ExplanationRequest request, CancellationToken ct)
    {
        // ── Affect gate: ConfusionResolving ──
        // D'Mello & Graesser (2012): confusion that resolves produces deep learning.
        // Auto-delivered content during productive confusion DISRUPTS resolution.
        if (request.ConfusionState == ConfusionState.ConfusionResolving)
        {
            _logger.LogDebug(
                "L3 gate: skipping generation for question {QuestionId} — " +
                "student is in productive confusion (ConfusionResolving). " +
                "DO NOT interrupt resolution.",
                request.QuestionId);
            return null;
        }

        // ── Affect gate: Bored_TooEasy ──
        // Over-explaining easy material reinforces boredom. Let the student
        // move on to harder content instead.
        if (request.DisengagementType == Services.DisengagementType.Bored_TooEasy)
        {
            _logger.LogDebug(
                "L3 gate: skipping generation for question {QuestionId} — " +
                "student is bored (Bored_TooEasy). Over-explanation would worsen disengagement.",
                request.QuestionId);
            return null;
        }

        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildUserPrompt(request);
        var maxTokens = DetermineMaxTokensByFocus(request.FocusLevel);

        var llmRequest = new LlmRequest(
            ModelId: "sonnet",
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Temperature: 0.3f,
            MaxTokens: maxTokens,
            CacheSystemPrompt: true);

        _logger.LogDebug(
            "L3 generating personalized explanation for question {QuestionId}, " +
            "focus={Focus}, confusion={Confusion}, mastery={Mastery:P0}, bloom={Bloom}, " +
            "scaffolding={Scaffolding}, methodology={Methodology}, maxTokens={MaxTokens}",
            request.QuestionId, request.FocusLevel, request.ConfusionState,
            request.MasteryProbability, request.BloomLevel,
            request.ScaffoldingLevel, request.Methodology, maxTokens);

        var response = await _llm.CompleteAsync(llmRequest, ct);

        // prr-046: per-feature cost tag on success path.
        _costMetric.Record(
            feature: "explanation-l3",
            tier: "tier3",
            task: "full_explanation",
            modelId: response.ModelId,
            inputTokens: response.InputTokens,
            outputTokens: response.OutputTokens);

        return new GeneratedExplanation(response.Content, response.ModelId, response.OutputTokens);
    }

    // =========================================================================
    // VERBOSITY SCALING BY FOCUS LEVEL
    // =========================================================================

    /// <summary>
    /// Flow=500 tokens (full depth), Drifting=300, Fatigued=200, Disengaged=150.
    /// Students with degraded focus need shorter, more direct explanations.
    /// </summary>
    private static int DetermineMaxTokensByFocus(FocusLevel focus)
    {
        return focus switch
        {
            FocusLevel.Flow => 500,
            FocusLevel.Engaged => 500,
            FocusLevel.Drifting => 300,
            FocusLevel.Fatigued => 200,
            FocusLevel.Disengaged => 150,
            FocusLevel.DisengagedBored => 150,
            FocusLevel.DisengagedExhausted => 150,
            _ => 300
        };
    }

    // =========================================================================
    // SYSTEM PROMPT (methodology + scaffolding + language + affect)
    // =========================================================================

    private static string BuildSystemPrompt(L3ExplanationRequest ctx)
    {
        var lang = MapLanguage(ctx.Language);
        var methodology = MapMethodology(ctx.Methodology);
        var scaffolding = MapScaffolding(ctx.ScaffoldingLevel);
        var depth = MapBloomsDepth(ctx.BloomLevel);
        var focusInstruction = MapFocusInstruction(ctx.FocusLevel);

        return $"""
            You are an expert {ctx.Subject} tutor. Respond ONLY in {lang}.

            METHODOLOGY: {methodology}
            DEPTH: {depth}
            SCAFFOLDING: {scaffolding}
            FOCUS STATE: {focusInstruction}

            RULES:
            - Address the specific misconception revealed by the student's answer.
            - Never mention the student's identity, name, or any personal information.
            - Be encouraging but precise.
            - Use mathematical notation where appropriate.
            - Keep the explanation focused on exactly one concept.
            - Adjust your language complexity to match the student's demonstrated level.
            """;
    }

    // =========================================================================
    // USER PROMPT (question + mastery + behavioral signals)
    // =========================================================================

    private static string BuildUserPrompt(L3ExplanationRequest ctx)
    {
        var parts = new List<string>(20);

        // ── Question context ──
        parts.Add($"QUESTION: {ctx.QuestionStem}");
        parts.Add($"CORRECT ANSWER: {ctx.CorrectAnswer}");
        parts.Add($"STUDENT'S ANSWER: {ctx.StudentAnswer}");
        parts.Add($"ERROR TYPE: {ctx.ErrorType}");

        if (!string.IsNullOrEmpty(ctx.DistractorRationale))
            parts.Add($"WHY STUDENT CHOSE THIS: {ctx.DistractorRationale}");

        // ── Mastery context (no PII) ──
        parts.Add($"CONCEPT MASTERY: {ctx.MasteryProbability:P0}");
        parts.Add($"RECALL STRENGTH: {ctx.RecallProbability:P0}");
        parts.Add($"PREREQUISITE READINESS (PSI): {ctx.Psi:P0}");

        if (ctx.QualityQuadrant != Mastery.MasteryQuality.Mastered)
            parts.Add($"LEARNING STATE: {MapQualityQuadrant(ctx.QualityQuadrant)}");

        if (ctx.RecentErrorTypes is { Count: > 0 })
            parts.Add($"RECENT ERROR PATTERN: {string.Join(", ", ctx.RecentErrorTypes)}");

        // ── Method history (which methods have been tried) ──
        if (ctx.MethodHistory is { Count: > 0 })
            parts.Add($"PREVIOUSLY TRIED METHODS: {string.Join(", ", ctx.MethodHistory)}");

        // ── Behavioral signals (indicate hesitation/confusion -- no PII) ──
        if (ctx.BackspaceCount > 3)
            parts.Add("NOTE: Student showed significant hesitation while answering " +
                       "(reconsidered their response multiple times).");

        if (ctx.AnswerChangeCount > 1)
            parts.Add($"NOTE: Student changed their answer {ctx.AnswerChangeCount} times, " +
                       "indicating uncertainty about the concept.");

        // ── Response time relative to baseline ──
        if (ctx.MedianResponseTimeMs > 0 && ctx.ResponseTimeMs > 0)
        {
            double ratio = ctx.ResponseTimeMs / ctx.MedianResponseTimeMs;
            if (ratio > 2.0)
                parts.Add("NOTE: Student took significantly longer than usual on this question.");
            else if (ratio < 0.5)
                parts.Add("NOTE: Student answered very quickly (may not have fully engaged).");
        }

        // ── Low PSI: prerequisite gaps ──
        if (ctx.Psi < 0.5)
            parts.Add("IMPORTANT: Student has weak prerequisite knowledge. " +
                       "Start the explanation from foundational concepts.");

        // ── Confusion state instruction ──
        if (ctx.ConfusionState == ConfusionState.ConfusionStuck)
            parts.Add("IMPORTANT: Student has been confused for several questions. " +
                       "Provide a very clear, step-by-step explanation. " +
                       "Start from the most basic relevant principle.");

        parts.Add("Generate the explanation now.");

        return string.Join("\n\n", parts);
    }

    // =========================================================================
    // MAPPING HELPERS
    // =========================================================================

    private static string MapLanguage(string language) => language.ToLowerInvariant() switch
    {
        "he" => "Hebrew (עברית)",
        "ar" => "Arabic (العربية)",
        "en" => "English",
        _ => "Hebrew (עברית)"
    };

    private static string MapMethodology(string methodology) =>
        methodology.ToLowerInvariant() switch
        {
            "socratic" =>
                "Use the Socratic method. Ask 1-2 guiding questions that lead the student " +
                "to discover their own mistake. Do NOT give the answer directly.",

            "workedexample" or "worked_example" or "worked-example" =>
                "Provide a step-by-step worked example. Show each step of the solution clearly, " +
                "explaining the reasoning behind each transition.",

            "drillandpractice" or "drill_and_practice" or "drill-and-practice" =>
                "Provide a brief, direct correction. State what went wrong and the correct approach " +
                "in 1-2 concise sentences. No lengthy elaboration.",

            "feynman" =>
                "Ask the student to explain their reasoning. Guide them to articulate " +
                "why they chose their answer and where their understanding breaks down.",

            "directinstruction" or "direct_instruction" or "direct-instruction" =>
                "Explain the solution step-by-step, clearly showing each reasoning step. " +
                "Be explicit about the logic at each transition.",

            "spacedrepetition" or "spaced_repetition" or "spaced-repetition" =>
                "Provide a concise review-focused explanation. Highlight the key fact or rule " +
                "the student needs to recall. Use mnemonics if appropriate.",

            "analogy" =>
                "Explain the concept using a concrete analogy or real-world parallel. " +
                "Map the abstract concept to something the student already understands.",

            "retrievalpractice" or "retrieval_practice" or "retrieval-practice" =>
                "Guide the student to retrieve the answer from memory. Provide a cue or prompt " +
                "rather than the full explanation.",

            "bloomsprogression" or "blooms_progression" or "blooms-progression" =>
                "Match explanation depth to the Bloom's level being tested. " +
                "Lower levels get definitions, higher levels get analytical comparisons.",

            "projectbased" or "project_based" or "project-based" =>
                "Connect the concept to a practical application or project context. " +
                "Show how this knowledge applies in a real scenario.",

            _ =>
                "Explain the correct approach clearly, addressing the specific error."
        };

    private static string MapScaffolding(Mastery.ScaffoldingLevel level) => level switch
    {
        Mastery.ScaffoldingLevel.Full =>
            "Provide a COMPLETE worked example. Show every step from start to finish " +
            "with clear reasoning at each transition.",

        Mastery.ScaffoldingLevel.Partial =>
            "Point out the specific step where the error occurred. Show the correct " +
            "approach for that step, but let the student complete the rest.",

        Mastery.ScaffoldingLevel.HintsOnly =>
            "Give ONE concise sentence pointing toward the correct approach. " +
            "Do not reveal the full solution.",

        Mastery.ScaffoldingLevel.None =>
            "The student is at an independent level. Provide minimal feedback only.",

        _ => "Adjust explanation depth to match the student's level."
    };

    private static string MapBloomsDepth(int bloomsLevel) => bloomsLevel switch
    {
        1 or 2 => "SIMPLE: Use plain language. One key point. A concrete example if helpful.",
        3 or 4 => "MODERATE: Explain the underlying principle. Connect to related concepts.",
        5 or 6 => "ANALYTICAL: Encourage deeper reasoning. Compare approaches. Ask 'why' not just 'what'.",
        _ => "MODERATE: Explain the underlying principle clearly."
    };

    private static string MapFocusInstruction(FocusLevel focus) => focus switch
    {
        FocusLevel.Flow or FocusLevel.Engaged =>
            "Student is focused. Provide a thorough explanation at appropriate depth.",
        FocusLevel.Drifting =>
            "Student's attention is wavering. Keep explanation concise and visually structured " +
            "(use bullet points or numbered steps).",
        FocusLevel.Fatigued =>
            "Student is fatigued. Keep explanation BRIEF (2-3 sentences max). " +
            "Focus on the single most important point.",
        FocusLevel.Disengaged or FocusLevel.DisengagedExhausted =>
            "Student is disengaged. Provide ONE key sentence only. " +
            "Make it memorable and actionable.",
        FocusLevel.DisengagedBored =>
            "Student finds material too easy. Be extremely brief.",
        _ =>
            "Provide a clear, moderately-paced explanation."
    };

    private static string MapQualityQuadrant(Mastery.MasteryQuality quality) => quality switch
    {
        Mastery.MasteryQuality.Effortful =>
            "Student is trying hard but struggling — be patient and encouraging",
        Mastery.MasteryQuality.Careless =>
            "Student knows the material but makes sloppy mistakes — be direct and brief",
        Mastery.MasteryQuality.Struggling =>
            "Student is having fundamental difficulty — start from basics",
        _ =>
            "Student is progressing normally"
    };
}
