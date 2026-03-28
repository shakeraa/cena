// =============================================================================
// Cena Platform — Hint Content Generator
// SAI-02: Pure function — generates hint text from existing data (zero LLM cost)
// 3-level hint ladder: Nudge → Eliminate → Reveal
// =============================================================================

using Cena.Actors.Questions;
using Cena.Actors.Serving;

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
    string? StudentAnswer);

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
            2 => GenerateEliminate(request),
            3 => GenerateReveal(request),
            _ => "Review the question carefully and consider each option."
        };

        return new HintContent(text, HasMoreHints: request.HintLevel < 3);
    }

    /// <summary>Level 1: reference prerequisite concepts.</summary>
    private static string GenerateNudge(HintRequest req)
    {
        if (req.PrerequisiteConceptNames.Count > 0)
        {
            var prereq = req.PrerequisiteConceptNames[0];
            return $"Before answering, think about **{prereq}**. How does it relate to what's being asked?";
        }

        return "Read the question stem again carefully. What concept is being tested?";
    }

    /// <summary>Level 2: eliminate one distractor using its rationale.</summary>
    private static string GenerateEliminate(HintRequest req)
    {
        // Find a wrong option with a rationale to eliminate
        // Prefer the option the student chose (if wrong), otherwise pick any wrong option with rationale
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

        // Fallback: eliminate a random wrong option without rationale
        var wrongOption = req.Options.FirstOrDefault(o => !o.IsCorrect);
        if (wrongOption is not null)
            return $"Option **{wrongOption.Label}** is not the correct answer. Focus on the remaining options.";

        return "Try eliminating the option that seems least likely. Which answer can you rule out?";
    }

    /// <summary>Level 3: show approach from explanation or generic guidance.</summary>
    private static string GenerateReveal(HintRequest req)
    {
        if (!string.IsNullOrEmpty(req.Explanation))
        {
            // Show the explanation as a hint (the approach, not the answer)
            return req.Explanation;
        }

        // Fallback: generic guidance referencing the correct option's rationale
        var correct = req.Options.FirstOrDefault(o => o.IsCorrect);
        if (correct is not null && !string.IsNullOrEmpty(correct.DistractorRationale))
            return $"The key insight: {correct.DistractorRationale}";

        return "Review each option carefully. The correct answer follows directly from the core concept being tested.";
    }
}
