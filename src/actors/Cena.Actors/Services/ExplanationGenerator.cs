// =============================================================================
// Cena Platform -- Explanation Generator (L3)
// SAI-04: Generates personalized explanations via LLM
//
// Builds methodology-aware, Bloom's-calibrated, fatigue-adjusted prompts.
// Generates in the student's language (he/ar/en).
// NEVER includes student ID or PII in prompts.
// =============================================================================

using Cena.Actors.Gateway;
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
    IReadOnlyList<string>? PrerequisiteConceptNames);

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

public sealed class ExplanationGenerator : IExplanationGenerator
{
    private readonly ILlmClient _llm;
    private readonly ILogger<ExplanationGenerator> _logger;

    public ExplanationGenerator(ILlmClient llm, ILogger<ExplanationGenerator> logger)
    {
        _llm = llm;
        _logger = logger;
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

        return $"""
            You are an expert {ctx.Subject} tutor. Respond ONLY in {lang}.

            METHODOLOGY: {methodology}
            DEPTH: {depth}

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

            _ =>
                "Explain the correct approach clearly, addressing the specific error."
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
