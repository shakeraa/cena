// =============================================================================
// Cena Platform — Hint Content Generator
// SAI-002: Pure function — generates hint text from existing data (zero LLM cost)
// 3-level hint ladder: Nudge → Scaffold → Reveal
//
// Level 1 (Nudge): Prerequisite-based prompt — lowest-mastery prerequisite.
// Level 2 (Scaffold): Error-type-based coaching from ConceptMasteryState.
// Level 3 (Reveal): Explanation verbatim, or graceful fallback.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Questions;

namespace Cena.Actors.Services;

public interface IHintGenerator
{
    HintContent Generate(HintRequest request);
}

public sealed record HintRequest(
    int HintLevel,
    string QuestionId,
    string ConceptId,
    IReadOnlyList<string> PrerequisiteConceptNames,
    IReadOnlyList<QuestionOptionState> Options,
    string? Explanation,
    string? StudentAnswer,
    IReadOnlyList<MasteryPrerequisiteEdge>? Prerequisites = null,
    ConceptMasteryState? ConceptState = null);

public sealed record HintContent(
    string Text,
    bool HasMoreHints);

public sealed class HintGenerator : IHintGenerator
{
    public HintContent Generate(HintRequest request)
    {
        var text = request.HintLevel switch
        {
            1 => GenerateNudge(request),
            2 => GenerateScaffold(request),
            3 => GenerateReveal(request),
            _ => "Review the question carefully and consider each option."
        };

        return new HintContent(text, HasMoreHints: request.HintLevel < 3);
    }

    /// <summary>
    /// Level 1 (Nudge): Pick the prerequisite with lowest student mastery.
    /// If prerequisites with names are available, use the first (sorted by strength desc,
    /// lowest mastery wins). Falls back to "Re-read the question carefully."
    /// </summary>
    private static string GenerateNudge(HintRequest req)
    {
        // Prefer prerequisite concept names (already resolved by the session actor)
        if (req.PrerequisiteConceptNames.Count > 0)
        {
            var prereq = req.PrerequisiteConceptNames[0];
            return $"Consider how **{prereq}** applies here.";
        }

        return "Re-read the question carefully.";
    }

    /// <summary>
    /// Level 2 (Scaffold): Check ConceptState.RecentErrors[] for dominant error type.
    /// Routes to error-type-specific coaching. Falls back to distractor elimination
    /// when no error history is available.
    /// </summary>
    private static string GenerateScaffold(HintRequest req)
    {
        // SAI-002: Error-type-based coaching from ConceptMasteryState
        if (req.ConceptState is { RecentErrors.Length: > 0 })
        {
            var dominant = GetDominantErrorType(req.ConceptState.RecentErrors);
            var coaching = dominant switch
            {
                ErrorType.Procedural => "Check your calculation step by step.",
                ErrorType.Conceptual => "Think about the definition of the concept being tested.",
                ErrorType.Careless => "Slow down. You know this.",
                ErrorType.Systematic => "Check your calculation step by step.",
                ErrorType.Transfer => "Think about the definition of the concept being tested.",
                ErrorType.Motivational => "Take a breath and try again. You can do this.",
                _ => null
            };

            if (coaching != null)
                return coaching;
        }

        // Fallback: distractor elimination (original Level 2 behavior)
        return GenerateDistractorElimination(req);
    }

    /// <summary>
    /// Level 3 (Reveal): Return Explanation if present, else graceful fallback.
    /// </summary>
    private static string GenerateReveal(HintRequest req)
    {
        if (!string.IsNullOrEmpty(req.Explanation))
            return req.Explanation;

        // Fallback: generic guidance referencing the correct option's rationale
        var correct = req.Options.FirstOrDefault(o => o.IsCorrect);
        if (correct is not null && !string.IsNullOrEmpty(correct.DistractorRationale))
            return $"The key insight: {correct.DistractorRationale}";

        return "Review each option carefully. The correct answer follows directly from the core concept being tested.";
    }

    /// <summary>
    /// Distractor elimination fallback for Level 2 when no error history is available.
    /// </summary>
    private static string GenerateDistractorElimination(HintRequest req)
    {
        QuestionOptionState? target = null;

        if (!string.IsNullOrEmpty(req.StudentAnswer))
        {
            target = req.Options.FirstOrDefault(o =>
                o.Label == req.StudentAnswer && !o.IsCorrect && !string.IsNullOrEmpty(o.DistractorRationale));
        }

        target ??= req.Options.FirstOrDefault(o =>
            !o.IsCorrect && !string.IsNullOrEmpty(o.DistractorRationale));

        if (target is not null)
            return $"You can rule out option **{target.Label}** — {target.DistractorRationale}";

        var wrongOption = req.Options.FirstOrDefault(o => !o.IsCorrect);
        if (wrongOption is not null)
            return $"Option **{wrongOption.Label}** is not the correct answer. Focus on the remaining options.";

        return "Try eliminating the option that seems least likely.";
    }

    /// <summary>
    /// Find the most frequent error type in the recent errors array.
    /// </summary>
    private static ErrorType GetDominantErrorType(ErrorType[] recentErrors)
    {
        if (recentErrors.Length == 0) return ErrorType.None;

        // Count occurrences of each non-None error type
        ErrorType dominant = ErrorType.None;
        int maxCount = 0;

        Span<int> counts = stackalloc int[7]; // ErrorType enum has 7 values
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
}
