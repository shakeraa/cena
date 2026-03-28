// =============================================================================
// Cena Platform -- Hint Generation Service (SAI-01b)
// Language-aware, template-based hint generation with 3-level progressive
// disclosure. NO LLM calls -- pure template expansion from existing data.
//
// Level 1 (Nudge): Prerequisite-based conceptual prompt
// Level 2 (Scaffold): Distractor elimination / procedural coaching
// Level 3 (Reveal): Full explanation or graceful fallback
//
// Language support: Hebrew (he), Arabic (ar), English (en)
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Questions;

namespace Cena.Actors.Services;

/// <summary>
/// Generates progressive hint content from existing question/concept data.
/// Template-based only -- no LLM calls.
/// </summary>
public interface IHintGenerationService
{
    HintGenerationContent GenerateHint(HintGenerationContext context);
}

/// <summary>
/// Context for hint generation, providing all data needed to construct a hint.
/// </summary>
public sealed record HintGenerationContext(
    int HintLevel,                                    // 1, 2, or 3
    string ConceptId,
    string QuestionStem,
    IReadOnlyList<string> PrerequisiteConceptIds,
    IReadOnlyList<string> PrerequisiteNames,
    string? DistractorRationale,                      // From QuestionOptionData.DistractorRationale
    ScaffoldingLevel ScaffoldingLevel,
    string Language,                                  // "he", "ar", "en"
    IReadOnlyList<QuestionOptionState>? Options = null,
    string? QuestionExplanation = null,               // L1 persisted explanation
    string? StudentAnswer = null,
    ConceptMasteryState? ConceptState = null);

/// <summary>
/// Result of hint generation.
/// </summary>
public sealed record HintGenerationContent(
    string HintText,                                  // Markdown+LaTeX
    bool HasMoreHints);

public sealed class HintGenerationService : IHintGenerationService
{
    public HintGenerationContent GenerateHint(HintGenerationContext context)
    {
        var maxLevel = context.ScaffoldingLevel switch
        {
            ScaffoldingLevel.Full => 3,
            ScaffoldingLevel.Partial => 2,
            ScaffoldingLevel.HintsOnly => 1,
            _ => 0
        };

        if (context.HintLevel > maxLevel)
        {
            return new HintGenerationContent(
                GetTemplate(context.Language, "no_more_hints"),
                HasMoreHints: false);
        }

        var text = context.HintLevel switch
        {
            1 => GenerateNudge(context),
            2 => GenerateScaffold(context),
            3 => GenerateReveal(context),
            _ => GetTemplate(context.Language, "review_carefully")
        };

        bool hasMore = context.HintLevel < maxLevel;
        return new HintGenerationContent(text, hasMore);
    }

    // =========================================================================
    // LEVEL 1 -- Conceptual Nudge (from prerequisites)
    // =========================================================================

    private static string GenerateNudge(HintGenerationContext ctx)
    {
        if (ctx.PrerequisiteNames.Count > 0)
        {
            var prereq = ctx.PrerequisiteNames[0];
            return GetTemplate(ctx.Language, "nudge_prerequisite", prereq);
        }

        return GetTemplate(ctx.Language, "nudge_generic");
    }

    // =========================================================================
    // LEVEL 2 -- Procedural Scaffold (from DistractorRationale or error type)
    // =========================================================================

    private static string GenerateScaffold(HintGenerationContext ctx)
    {
        // Error-type-based coaching when ConceptState has error history
        if (ctx.ConceptState is { RecentErrors.Length: > 0 })
        {
            var dominant = GetDominantErrorType(ctx.ConceptState.RecentErrors);
            var coaching = GetErrorTypeCoaching(ctx.Language, dominant);
            if (coaching != null)
                return coaching;
        }

        // Distractor elimination from student's wrong answer
        if (ctx.Options is { Count: > 0 })
        {
            var eliminated = FindEliminatableDistractor(ctx);
            if (eliminated != null)
                return eliminated;
        }

        // Fallback: use DistractorRationale directly if provided
        if (!string.IsNullOrEmpty(ctx.DistractorRationale))
        {
            return GetTemplate(ctx.Language, "scaffold_rationale", ctx.DistractorRationale);
        }

        return GetTemplate(ctx.Language, "scaffold_generic");
    }

    // =========================================================================
    // LEVEL 3 -- Reveal (L1 explanation or fallback)
    // =========================================================================

    private static string GenerateReveal(HintGenerationContext ctx)
    {
        // Prefer the persisted explanation (from Task 01a or question authoring)
        if (!string.IsNullOrEmpty(ctx.QuestionExplanation))
            return ctx.QuestionExplanation;

        // Fallback: correct option's DistractorRationale
        if (ctx.Options is { Count: > 0 })
        {
            var correct = ctx.Options.FirstOrDefault(o => o.IsCorrect);
            if (correct is not null && !string.IsNullOrEmpty(correct.DistractorRationale))
            {
                return GetTemplate(ctx.Language, "reveal_insight", correct.DistractorRationale);
            }
        }

        // Final fallback: generic review guidance
        if (ctx.PrerequisiteNames.Count > 0)
        {
            return GetTemplate(ctx.Language, "reveal_fallback_concept", ctx.PrerequisiteNames[0]);
        }

        return GetTemplate(ctx.Language, "reveal_fallback_generic");
    }

    // =========================================================================
    // DISTRACTOR ELIMINATION HELPER
    // =========================================================================

    private static string? FindEliminatableDistractor(HintGenerationContext ctx)
    {
        QuestionOptionState? target = null;

        // Prefer the student's chosen wrong answer
        if (!string.IsNullOrEmpty(ctx.StudentAnswer))
        {
            target = ctx.Options!.FirstOrDefault(o =>
                o.Label == ctx.StudentAnswer && !o.IsCorrect
                && !string.IsNullOrEmpty(o.DistractorRationale));
        }

        // Fallback: any wrong option with rationale
        target ??= ctx.Options!.FirstOrDefault(o =>
            !o.IsCorrect && !string.IsNullOrEmpty(o.DistractorRationale));

        if (target is not null)
            return GetTemplate(ctx.Language, "eliminate_option", target.Label, target.DistractorRationale!);

        // Bare elimination without rationale
        var wrong = ctx.Options!.FirstOrDefault(o => !o.IsCorrect);
        if (wrong is not null)
            return GetTemplate(ctx.Language, "eliminate_bare", wrong.Label);

        return null;
    }

    // =========================================================================
    // ERROR TYPE COACHING
    // =========================================================================

    private static string? GetErrorTypeCoaching(string language, ErrorType dominant)
    {
        return dominant switch
        {
            ErrorType.Procedural => GetTemplate(language, "coaching_procedural"),
            ErrorType.Conceptual => GetTemplate(language, "coaching_conceptual"),
            ErrorType.Careless => GetTemplate(language, "coaching_careless"),
            ErrorType.Systematic => GetTemplate(language, "coaching_procedural"),
            ErrorType.Transfer => GetTemplate(language, "coaching_conceptual"),
            ErrorType.Motivational => GetTemplate(language, "coaching_motivational"),
            _ => null
        };
    }

    private static ErrorType GetDominantErrorType(ErrorType[] recentErrors)
    {
        if (recentErrors.Length == 0) return ErrorType.None;

        Span<int> counts = stackalloc int[7];
        ErrorType dominant = ErrorType.None;
        int maxCount = 0;

        foreach (var err in recentErrors)
        {
            if (err == ErrorType.None) continue;
            int idx = (int)err;
            if (idx < counts.Length)
            {
                counts[idx]++;
                if (counts[idx] > maxCount)
                {
                    maxCount = counts[idx];
                    dominant = err;
                }
            }
        }

        return dominant;
    }

    // =========================================================================
    // LANGUAGE TEMPLATES
    //
    // Simple string templates per language. Hebrew and Arabic are RTL but the
    // markdown rendering handles directionality -- we just provide the text.
    // Placeholders: {0}, {1}, etc. via string.Format.
    // =========================================================================

    private static string GetTemplate(string language, string key, params string[] args)
    {
        var template = (language, key) switch
        {
            // ── English ──
            ("en", "nudge_prerequisite") => "Think about how **{0}** applies here.",
            ("en", "nudge_generic") => "Re-read the question carefully and consider what concept is being tested.",
            ("en", "scaffold_rationale") => "Consider this: {0}",
            ("en", "scaffold_generic") => "Try eliminating the option that seems least likely.",
            ("en", "eliminate_option") => "You can rule out option **{0}** -- {1}",
            ("en", "eliminate_bare") => "Option **{0}** is not the correct answer. Focus on the remaining options.",
            ("en", "reveal_insight") => "The key insight: {0}",
            ("en", "reveal_fallback_concept") => "Review the concept **{0}** in your study materials.",
            ("en", "reveal_fallback_generic") => "Review each option carefully. The correct answer follows directly from the core concept being tested.",
            ("en", "no_more_hints") => "No more hints available at this scaffolding level.",
            ("en", "review_carefully") => "Review the question carefully and consider each option.",
            ("en", "coaching_procedural") => "Check your calculation step by step.",
            ("en", "coaching_conceptual") => "Think about the definition of the concept being tested.",
            ("en", "coaching_careless") => "Slow down. You know this -- read the question again carefully.",
            ("en", "coaching_motivational") => "Take a breath and try again. You can do this.",

            // ── Hebrew ──
            ("he", "nudge_prerequisite") => "\u202B\u05D7\u05E9\u05D1\u05D5 \u05D0\u05D9\u05DA **{0}** \u05E7\u05E9\u05D5\u05E8 \u05DC\u05DB\u05D0\u05DF.\u202C",
            ("he", "nudge_generic") => "\u202B\u05E7\u05E8\u05D0\u05D5 \u05E9\u05D5\u05D1 \u05D0\u05EA \u05D4\u05E9\u05D0\u05DC\u05D4 \u05D1\u05EA\u05E9\u05D5\u05DE\u05EA \u05DC\u05D1.\u202C",
            ("he", "scaffold_rationale") => "\u202B\u05D7\u05E9\u05D1\u05D5 \u05E2\u05DC \u05D6\u05D4: {0}\u202C",
            ("he", "scaffold_generic") => "\u202B\u05E0\u05E1\u05D5 \u05DC\u05E4\u05E1\u05D5\u05DC \u05D0\u05EA \u05D4\u05EA\u05E9\u05D5\u05D1\u05D4 \u05E9\u05E0\u05E8\u05D0\u05D9\u05EA \u05D4\u05DB\u05D9 \u05E4\u05D7\u05D5\u05EA \u05E1\u05D1\u05D9\u05E8\u05D4.\u202C",
            ("he", "eliminate_option") => "\u202B\u05D0\u05E4\u05E9\u05E8 \u05DC\u05E4\u05E1\u05D5\u05DC \u05D0\u05EA \u05D0\u05E4\u05E9\u05E8\u05D5\u05EA **{0}** -- {1}\u202C",
            ("he", "eliminate_bare") => "\u202B\u05D0\u05E4\u05E9\u05E8\u05D5\u05EA **{0}** \u05D0\u05D9\u05E0\u05D4 \u05D4\u05EA\u05E9\u05D5\u05D1\u05D4 \u05D4\u05E0\u05DB\u05D5\u05E0\u05D4. \u05D4\u05EA\u05E8\u05DB\u05D6\u05D5 \u05D1\u05D0\u05E4\u05E9\u05E8\u05D5\u05D9\u05D5\u05EA \u05D4\u05E0\u05D5\u05EA\u05E8\u05D5\u05EA.\u202C",
            ("he", "reveal_insight") => "\u202B\u05D4\u05EA\u05D5\u05D1\u05E0\u05D4 \u05D4\u05DE\u05E8\u05DB\u05D6\u05D9\u05EA: {0}\u202C",
            ("he", "reveal_fallback_concept") => "\u202B\u05E2\u05D9\u05D9\u05E0\u05D5 \u05D1\u05DE\u05D5\u05E9\u05D2 **{0}** \u05D1\u05D7\u05D5\u05DE\u05E8 \u05D4\u05DC\u05D9\u05DE\u05D5\u05D3 \u05E9\u05DC\u05DB\u05DD.\u202C",
            ("he", "reveal_fallback_generic") => "\u202B\u05E2\u05D9\u05D9\u05E0\u05D5 \u05D1\u05DB\u05DC \u05D0\u05E4\u05E9\u05E8\u05D5\u05EA \u05D1\u05D6\u05D4\u05D9\u05E8\u05D5\u05EA. \u05D4\u05EA\u05E9\u05D5\u05D1\u05D4 \u05D4\u05E0\u05DB\u05D5\u05E0\u05D4 \u05E0\u05D5\u05D1\u05E2\u05EA \u05D9\u05E9\u05D9\u05E8\u05D5\u05EA \u05DE\u05D4\u05DE\u05D5\u05E9\u05D2 \u05D4\u05E0\u05D1\u05D3\u05E7.\u202C",
            ("he", "no_more_hints") => "\u202B\u05D0\u05D9\u05DF \u05E2\u05D5\u05D3 \u05E8\u05DE\u05D6\u05D9\u05DD \u05D6\u05DE\u05D9\u05E0\u05D9\u05DD \u05D1\u05E8\u05DE\u05D4 \u05D6\u05D5.\u202C",
            ("he", "review_carefully") => "\u202B\u05E7\u05E8\u05D0\u05D5 \u05D0\u05EA \u05D4\u05E9\u05D0\u05DC\u05D4 \u05D1\u05D6\u05D4\u05D9\u05E8\u05D5\u05EA \u05D5\u05E9\u05E7\u05DC\u05D5 \u05DB\u05DC \u05D0\u05E4\u05E9\u05E8\u05D5\u05EA.\u202C",
            ("he", "coaching_procedural") => "\u202B\u05D1\u05D3\u05E7\u05D5 \u05D0\u05EA \u05D4\u05D7\u05D9\u05E9\u05D5\u05D1 \u05E6\u05E2\u05D3 \u05D0\u05D7\u05E8 \u05E6\u05E2\u05D3.\u202C",
            ("he", "coaching_conceptual") => "\u202B\u05D7\u05E9\u05D1\u05D5 \u05E2\u05DC \u05D4\u05D4\u05D2\u05D3\u05E8\u05D4 \u05E9\u05DC \u05D4\u05DE\u05D5\u05E9\u05D2 \u05D4\u05E0\u05D1\u05D3\u05E7.\u202C",
            ("he", "coaching_careless") => "\u202B\u05D4\u05D0\u05D8 \u05E7\u05E6\u05EA. \u05D0\u05EA\u05DD \u05D9\u05D5\u05D3\u05E2\u05D9\u05DD \u05D0\u05EA \u05D6\u05D4 -- \u05E7\u05E8\u05D0\u05D5 \u05E9\u05D5\u05D1 \u05D0\u05EA \u05D4\u05E9\u05D0\u05DC\u05D4.\u202C",
            ("he", "coaching_motivational") => "\u202B\u05E7\u05D7\u05D5 \u05E0\u05E9\u05D9\u05DE\u05D4 \u05E2\u05DE\u05D5\u05E7\u05D4 \u05D5\u05E0\u05E1\u05D5 \u05E9\u05D5\u05D1. \u05D0\u05EA\u05DD \u05D9\u05DB\u05D5\u05DC\u05D9\u05DD.\u202C",

            // ── Arabic ──
            ("ar", "nudge_prerequisite") => "\u202B\u0641\u0643\u0651\u0631 \u0643\u064A\u0641 \u064A\u0631\u062A\u0628\u0637 **{0}** \u0628\u0647\u0630\u0627 \u0627\u0644\u0633\u0624\u0627\u0644.\u202C",
            ("ar", "nudge_generic") => "\u202B\u0623\u0639\u062F \u0642\u0631\u0627\u0621\u0629 \u0627\u0644\u0633\u0624\u0627\u0644 \u0628\u0639\u0646\u0627\u064A\u0629.\u202C",
            ("ar", "scaffold_rationale") => "\u202B\u0641\u0643\u0651\u0631 \u0641\u064A \u0647\u0630\u0627: {0}\u202C",
            ("ar", "scaffold_generic") => "\u202B\u062D\u0627\u0648\u0644 \u0627\u0633\u062A\u0628\u0639\u0627\u062F \u0627\u0644\u0625\u062C\u0627\u0628\u0629 \u0627\u0644\u0623\u0642\u0644 \u0627\u062D\u062A\u0645\u0627\u0644\u0627\u064B.\u202C",
            ("ar", "eliminate_option") => "\u202B\u064A\u0645\u0643\u0646\u0643 \u0627\u0633\u062A\u0628\u0639\u0627\u062F \u0627\u0644\u062E\u064A\u0627\u0631 **{0}** -- {1}\u202C",
            ("ar", "eliminate_bare") => "\u202B\u0627\u0644\u062E\u064A\u0627\u0631 **{0}** \u0644\u064A\u0633 \u0627\u0644\u0625\u062C\u0627\u0628\u0629 \u0627\u0644\u0635\u062D\u064A\u062D\u0629. \u0631\u0643\u0651\u0632 \u0639\u0644\u0649 \u0627\u0644\u062E\u064A\u0627\u0631\u0627\u062A \u0627\u0644\u0645\u062A\u0628\u0642\u064A\u0629.\u202C",
            ("ar", "reveal_insight") => "\u202B\u0627\u0644\u0641\u0643\u0631\u0629 \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629: {0}\u202C",
            ("ar", "reveal_fallback_concept") => "\u202B\u0631\u0627\u062C\u0639 \u0645\u0641\u0647\u0648\u0645 **{0}** \u0641\u064A \u0645\u0648\u0627\u062F \u0627\u0644\u062F\u0631\u0627\u0633\u0629.\u202C",
            ("ar", "reveal_fallback_generic") => "\u202B\u0631\u0627\u062C\u0639 \u0643\u0644 \u062E\u064A\u0627\u0631 \u0628\u0639\u0646\u0627\u064A\u0629. \u0627\u0644\u0625\u062C\u0627\u0628\u0629 \u0627\u0644\u0635\u062D\u064A\u062D\u0629 \u062A\u0646\u0628\u0639 \u0645\u0628\u0627\u0634\u0631\u0629 \u0645\u0646 \u0627\u0644\u0645\u0641\u0647\u0648\u0645 \u0627\u0644\u0623\u0633\u0627\u0633\u064A.\u202C",
            ("ar", "no_more_hints") => "\u202B\u0644\u0627 \u062A\u0648\u062C\u062F \u062A\u0644\u0645\u064A\u062D\u0627\u062A \u0623\u062E\u0631\u0649 \u0645\u062A\u0627\u062D\u0629.\u202C",
            ("ar", "review_carefully") => "\u202B\u0631\u0627\u062C\u0639 \u0627\u0644\u0633\u0624\u0627\u0644 \u0628\u0639\u0646\u0627\u064A\u0629 \u0648\u0641\u0643\u0651\u0631 \u0641\u064A \u0643\u0644 \u062E\u064A\u0627\u0631.\u202C",
            ("ar", "coaching_procedural") => "\u202B\u062A\u062D\u0642\u0642 \u0645\u0646 \u0627\u0644\u062D\u0633\u0627\u0628 \u062E\u0637\u0648\u0629 \u0628\u062E\u0637\u0648\u0629.\u202C",
            ("ar", "coaching_conceptual") => "\u202B\u0641\u0643\u0651\u0631 \u0641\u064A \u062A\u0639\u0631\u064A\u0641 \u0627\u0644\u0645\u0641\u0647\u0648\u0645 \u0627\u0644\u0630\u064A \u064A\u062A\u0645 \u0627\u062E\u062A\u0628\u0627\u0631\u0647.\u202C",
            ("ar", "coaching_careless") => "\u202B\u062A\u0645\u0647\u0651\u0644. \u0623\u0646\u062A \u062A\u0639\u0631\u0641 \u0647\u0630\u0627 -- \u0623\u0639\u062F \u0642\u0631\u0627\u0621\u0629 \u0627\u0644\u0633\u0624\u0627\u0644.\u202C",
            ("ar", "coaching_motivational") => "\u202B\u062E\u0630 \u0646\u0641\u0633\u0627\u064B \u0639\u0645\u064A\u0642\u0627\u064B \u0648\u062D\u0627\u0648\u0644 \u0645\u0631\u0629 \u0623\u062E\u0631\u0649. \u064A\u0645\u0643\u0646\u0643 \u0641\u0639\u0644 \u0630\u0644\u0643.\u202C",

            // ── Default: fall through to English ──
            _ => GetEnglishFallback(key)
        };

        return args.Length > 0 ? string.Format(template, args) : template;
    }

    /// <summary>
    /// Fallback to English for unknown language codes.
    /// </summary>
    private static string GetEnglishFallback(string key) => key switch
    {
        "nudge_prerequisite" => "Think about how **{0}** applies here.",
        "nudge_generic" => "Re-read the question carefully and consider what concept is being tested.",
        "scaffold_rationale" => "Consider this: {0}",
        "scaffold_generic" => "Try eliminating the option that seems least likely.",
        "eliminate_option" => "You can rule out option **{0}** -- {1}",
        "eliminate_bare" => "Option **{0}** is not the correct answer. Focus on the remaining options.",
        "reveal_insight" => "The key insight: {0}",
        "reveal_fallback_concept" => "Review the concept **{0}** in your study materials.",
        "reveal_fallback_generic" => "Review each option carefully. The correct answer follows directly from the core concept being tested.",
        "no_more_hints" => "No more hints available at this scaffolding level.",
        "review_carefully" => "Review the question carefully and consider each option.",
        "coaching_procedural" => "Check your calculation step by step.",
        "coaching_conceptual" => "Think about the definition of the concept being tested.",
        "coaching_careless" => "Slow down. You know this -- read the question again carefully.",
        "coaching_motivational" => "Take a breath and try again. You can do this.",
        _ => "Review the question carefully."
    };
}
