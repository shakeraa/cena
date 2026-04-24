// =============================================================================
// Cena Platform — Static Hint Ladder Fallback (prr-012)
//
// When a session hits the 3-call Socratic LLM cap OR the global cost circuit
// breaker is open, ClaudeTutorLlmService routes the turn here instead of
// calling Anthropic. No LLM, no per-turn cost, deterministic fallback.
//
// The ladder has three rungs:
//   L1 "Try this step"          — nudges the student to re-read the problem and
//                                 name the first operation.
//   L2 "Here's the method"      — names the canonical method for the subject
//                                 (e.g. "isolate the variable", "factor first").
//   L3 "Worked-example pointer" — tells the student to look at a worked
//                                 example in their textbook / course materials.
//
// Rung selection is sequential by call index (0-based): the first fallback
// turn gets L1, the second L2, the third and beyond L3. This matches how the
// old Socratic flow naturally escalated via LLM turns and keeps the UX
// predictable when the student notices the shift to static copy.
//
// ADR-0003 constraint: no per-student misconception state on the profile.
// This service is pure — it looks only at the TutorContext passed in.
//
// Ship-gate ban compliance (GD-004): copy is neutral ("Try looking at this
// step again", not "Don't lose your streak!"). No loss-aversion, no variable
// rewards, no streaks.
// =============================================================================

namespace Cena.Actors.Tutor;

/// <summary>
/// Deterministic fallback used when the Socratic LLM budget is exhausted
/// or cost circuit breakers are tripped. Never calls an LLM.
/// </summary>
public interface IStaticHintLadderFallback
{
    /// <summary>
    /// Produces a hint for the given tutoring context + the current fallback
    /// index. Index is 0-based across fallback turns within the session.
    /// </summary>
    StaticHintResponse GetHint(TutorContext context, int fallbackIndex);
}

/// <summary>
/// A rendered static hint. <see cref="Rung"/> is exposed for telemetry so
/// the admin dashboard can track which rung students see most.
/// </summary>
public sealed record StaticHintResponse(string Text, StaticHintRung Rung);

public enum StaticHintRung
{
    L1_TryThisStep = 1,
    L2_HereIsTheMethod = 2,
    L3_WorkedExample = 3
}

/// <summary>
/// Production implementation of the static hint ladder. Pre-written copy,
/// subject-light (we don't try to re-create the LLM's subject expertise —
/// that's the whole point of falling back), and ship-gate compliant.
/// </summary>
public sealed class StaticHintLadderFallback : IStaticHintLadderFallback
{
    public StaticHintResponse GetHint(TutorContext context, int fallbackIndex)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (fallbackIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(fallbackIndex), "fallbackIndex must be non-negative");

        var subject = string.IsNullOrWhiteSpace(context.Subject)
            ? "this problem"
            : context.Subject;

        return fallbackIndex switch
        {
            0 => new StaticHintResponse(
                $"Let's look at this step-by-step. Re-read the problem carefully — what is the very first operation you need to perform? Write it down before moving on.",
                StaticHintRung.L1_TryThisStep),

            1 => new StaticHintResponse(
                $"Think about the method that usually applies to {subject} at your level — isolate what's unknown, or simplify what you can factor. Pick one technique and apply it to the first line.",
                StaticHintRung.L2_HereIsTheMethod),

            _ => new StaticHintResponse(
                $"You've put in solid effort on this one. Try looking at a worked example for {subject} in your textbook or notes, then come back to this problem with fresh eyes. You can pick this up again in your next session.",
                StaticHintRung.L3_WorkedExample)
        };
    }
}
