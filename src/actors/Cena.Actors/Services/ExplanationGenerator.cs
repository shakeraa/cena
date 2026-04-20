// =============================================================================
// Cena Platform -- Explanation Generator (L3)
// SAI-04: Generates personalized explanations via LLM
//
// Builds methodology-aware, Bloom's-calibrated, fatigue-adjusted prompts.
// Generates in the student's language (he/ar/en).
// NEVER includes student ID or PII in prompts.
// =============================================================================

using Cena.Actors.Gateway;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Full context for generating a personalized explanation.
/// Contains question data, error classification, and optional L3 student signals.
/// </summary>
public sealed record ExplanationContext(
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    string ErrorType,
    string Methodology,
    string? DistractorRationale,
    int BloomsLevel,
    string Subject,
    string Language,
    // L3 personalization signals (all optional)
    double? ConceptMastery,
    string? FatigueLevel,
    int? BackspaceCount,
    int? AnswerChangeCount,
    IReadOnlyList<string>? RecentErrorTypes,
    IReadOnlyList<string>? PrerequisiteConceptNames,
    // SAI-003: Scaffolding depth for prompt calibration
    string? ScaffoldingLevel = null,
    // SAI-008: Question difficulty for difficulty-aware framing
    float? QuestionDifficulty = null);

/// <summary>
/// The LLM-generated explanation with model metadata.
/// </summary>
public sealed record GeneratedExplanation(string Text, string ModelId, int TokenCount);

/// <summary>
/// Generates misconception-specific explanations using LLM.
/// </summary>
public interface IExplanationGenerator
{
    Task<GeneratedExplanation> GenerateAsync(ExplanationContext context, CancellationToken ct);
}

// ADR-0045: Methodology-aware misconception explanation via Sonnet (256-768 tokens).
// Sonnet sufficient per routing-config §feynman_explanation notes — Haiku quality
// insufficient for grading-grade explanation. Shares the `full_explanation` routing
// row with L3ExplanationGenerator. See contracts/llm/routing-config.yaml.
//
// prr-047: legacy generator kept for the non-personalized path. Callers
// (notably PersonalizedExplanationService.FallbackToL2Async and the explanation
// orchestrator) are responsible for the IExplanationCacheService lookup BEFORE
// invoking this service, so the cache guard lives at the call site, not here.
// Downstream we still cache the output back into L2 Redis via the orchestrator.
// prr-046: finops cost-center "explanation-l2". Shares the `full_explanation`
// routing row with L3ExplanationGenerator but bills separately so finops can
// see the L2/L3 split in the cost-projection dashboard.
[TaskRouting("tier3", "full_explanation")]
[FeatureTag("explanation-l2")]
[AllowsUncachedLlm("Caller performs IExplanationCacheService.GetAsync before invoking this generator; result is cached by the orchestrator on success.")]
public sealed class ExplanationGenerator : IExplanationGenerator
{
    private readonly ILlmClient _llm;
    private readonly ILogger<ExplanationGenerator> _logger;
    private readonly ILlmCostMetric _costMetric;

    public ExplanationGenerator(
        ILlmClient llm,
        ILogger<ExplanationGenerator> logger,
        ILlmCostMetric costMetric)
    {
        _llm = llm;
        _logger = logger;
        _costMetric = costMetric;
    }

    public async Task<GeneratedExplanation> GenerateAsync(ExplanationContext context, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(context);
        var userPrompt = BuildUserPrompt(context);
        var maxTokens = DetermineMaxTokens(context);

        var request = new LlmRequest(
            ModelId: "sonnet",
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Temperature: 0.3f,
            MaxTokens: maxTokens);

        _logger.LogDebug(
            "Generating explanation for error {ErrorType}, methodology {Methodology}, language {Language}",
            context.ErrorType, context.Methodology, context.Language);

        var response = await _llm.CompleteAsync(request, ct);

        // prr-046: per-feature cost tag on success path.
        _costMetric.Record(
            feature: "explanation-l2",
            tier: "tier3",
            task: "full_explanation",
            modelId: response.ModelId,
            inputTokens: response.InputTokens,
            outputTokens: response.OutputTokens);

        return new GeneratedExplanation(response.Content, response.ModelId, response.OutputTokens);
    }

    // =========================================================================
    // PROMPT CONSTRUCTION
    // =========================================================================

    private static string BuildSystemPrompt(ExplanationContext ctx)
    {
        var lang = MapLanguage(ctx.Language);
        var methodology = MapMethodology(ctx.Methodology);
        var depth = MapBloomsDepth(ctx.BloomsLevel);
        var scaffolding = MapScaffoldingDepth(ctx.ScaffoldingLevel);

        return $"""
            You are an expert {ctx.Subject} tutor. Respond ONLY in {lang}.

            METHODOLOGY: {methodology}
            DEPTH: {depth}
            SCAFFOLDING: {scaffolding}

            RULES:
            - Address the specific misconception revealed by the student's answer.
            - Never mention the student's identity, name, or any personal information.
            - Be encouraging but precise.
            - Use mathematical notation where appropriate.
            - Keep the explanation focused on exactly one concept.
            """;
    }

    private static string BuildUserPrompt(ExplanationContext ctx)
    {
        var parts = new List<string>(12);

        parts.Add($"QUESTION: {ctx.QuestionStem}");
        parts.Add($"CORRECT ANSWER: {ctx.CorrectAnswer}");
        parts.Add($"STUDENT'S ANSWER: {ctx.StudentAnswer}");
        parts.Add($"ERROR TYPE: {ctx.ErrorType}");

        if (!string.IsNullOrEmpty(ctx.DistractorRationale))
            parts.Add($"WHY STUDENT CHOSE THIS: {ctx.DistractorRationale}");

        // L3 personalization signals (no PII)
        if (ctx.ConceptMastery.HasValue)
            parts.Add($"CONCEPT MASTERY: {ctx.ConceptMastery.Value:P0}");

        if (ctx.RecentErrorTypes is { Count: > 0 })
            parts.Add($"RECENT ERROR PATTERN: {string.Join(", ", ctx.RecentErrorTypes)}");

        if (ctx.PrerequisiteConceptNames is { Count: > 0 })
            parts.Add($"PREREQUISITE CONCEPTS: {string.Join(", ", ctx.PrerequisiteConceptNames)}");

        // Behavioral signals that indicate hesitation/confusion (no PII)
        if (ctx.BackspaceCount is > 3)
            parts.Add("NOTE: Student showed significant hesitation while answering.");

        if (ctx.AnswerChangeCount is > 1)
            parts.Add($"NOTE: Student changed their answer {ctx.AnswerChangeCount} times, indicating uncertainty.");

        // Fatigue adjustment instruction
        if (ctx.FatigueLevel is "high" or "critical")
            parts.Add("IMPORTANT: Student is fatigued. Keep explanation BRIEF (2-3 sentences max).");

        // SAI-008: Difficulty-aware framing for L2 generation
        if (ctx.QuestionDifficulty.HasValue && ctx.ConceptMastery.HasValue)
        {
            var gap = DifficultyGap.Compute(ctx.QuestionDifficulty.Value, (float)ctx.ConceptMastery.Value);
            var frame = DifficultyGap.Classify(gap);
            var promptFrame = DifficultyGap.ToPromptFrame(frame);
            if (!string.IsNullOrEmpty(promptFrame))
                parts.Add($"DIFFICULTY CONTEXT: {promptFrame}");
        }

        parts.Add("Generate the explanation now.");

        return string.Join("\n\n", parts);
    }

    // =========================================================================
    // METHODOLOGY MAPPING
    // =========================================================================

    private static string MapMethodology(string methodology)
    {
        return methodology.ToLowerInvariant() switch
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
                "the student needs to recall.",

            "retrievalpractice" or "retrieval_practice" or "retrieval-practice" =>
                "Guide the student to retrieve the answer from memory. Provide a cue or prompt " +
                "rather than the full explanation.",

            "analogy" =>
                "Explain the concept using a concrete analogy or real-world parallel.",

            "bloomsprogression" or "blooms_progression" or "blooms-progression" =>
                "Match explanation depth to the Bloom's level being tested.",

            _ =>
                "Explain the correct approach clearly, addressing the specific error."
        };
    }

    // =========================================================================
    // SAI-003: SCAFFOLDING DEPTH MAPPING
    // =========================================================================

    private static string MapScaffoldingDepth(string? scaffoldingLevel)
    {
        return (scaffoldingLevel?.ToLowerInvariant()) switch
        {
            "full" =>
                "Provide a COMPLETE worked example. Show every step from start to finish " +
                "with clear reasoning at each transition.",

            "partial" =>
                "Point out the specific step where the error occurred. Show the correct " +
                "approach for that step, but let the student complete the rest.",

            "hintsonly" =>
                "Give ONE concise sentence pointing toward the correct approach. " +
                "Do not reveal the full solution.",

            "none" =>
                "The student is at an independent level. Provide minimal feedback only.",

            _ =>
                "Adjust explanation depth to match the student's level."
        };
    }

    // =========================================================================
    // BLOOM'S DEPTH CALIBRATION
    // =========================================================================

    private static string MapBloomsDepth(int bloomsLevel)
    {
        return bloomsLevel switch
        {
            1 or 2 => "SIMPLE: Use plain language. One key point. A concrete example if helpful.",
            3 or 4 => "MODERATE: Explain the underlying principle. Connect to related concepts.",
            5 or 6 => "ANALYTICAL: Encourage deeper reasoning. Compare approaches. Ask 'why' not just 'what'.",
            _ => "MODERATE: Explain the underlying principle clearly."
        };
    }

    // =========================================================================
    // LANGUAGE MAPPING
    // =========================================================================

    private static string MapLanguage(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "he" => "Hebrew (עברית)",
            "ar" => "Arabic (العربية)",
            "en" => "English",
            _ => "Hebrew (עברית)" // Default to Hebrew for Israeli students
        };
    }

    // =========================================================================
    // TOKEN BUDGET
    // =========================================================================

    private static int DetermineMaxTokens(ExplanationContext ctx)
    {
        // Fatigued students get shorter explanations
        if (ctx.FatigueLevel is "high" or "critical")
            return 256;

        // Drill methodology = brief
        if (ctx.Methodology.Contains("drill", StringComparison.OrdinalIgnoreCase))
            return 256;

        // Higher Bloom's = more room for analysis
        return ctx.BloomsLevel switch
        {
            1 or 2 => 384,
            3 or 4 => 512,
            5 or 6 => 768,
            _ => 512
        };
    }
}
