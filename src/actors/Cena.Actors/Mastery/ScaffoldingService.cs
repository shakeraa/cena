// =============================================================================
// Cena Platform -- Scaffolding Service
// MST-011: Maps effective mastery + PSI to scaffolding level
//
// prr-041 (2026-04-20): worked-example fading hysteresis bolted onto the
// level-decision surface. The mastery→level map remains pure stateless. The
// hysteresis is a separate decision on a *history of attempts at a fixed
// level* — it answers "given the student just took N attempts at scaffold L,
// should they fade down, restore up, or hold?" without touching BKT / PSI /
// effective mastery. Two-surface separation is deliberate: mastery can
// oscillate freely inside a single scaffold level without thrashing the
// student's UI copy.
//
// Rules (ADR to be authored in the parallel branch — cite here once merged):
//   - Minimum 2 attempts at any level before evaluation (prevents snap decisions)
//   - 3 consecutive correct at current level → fade to L-1 (less support)
//   - Any incorrect at a faded level → restore to L (more support)
//   - Never fade below minLevel
//
// Integer-axis convention for DecideNextScaffoldLevel:
//   - HIGHER int = MORE support (L in the spec)
//   - fade  = -1 (L → L-1)
//   - restore = +1 (L-1 → L)
//   - minLevel = floor (fading stops here)
//   - caller maps ScaffoldingLevel ↔ int (e.g. supportLevel = 3 - (int)scaffoldingLevel)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// One student attempt under a known scaffold level. prr-041 hysteresis input.
/// Passed as a chronological list (oldest → newest) to
/// <see cref="ScaffoldingService.DecideNextScaffoldLevel"/>.
///
/// <paramref name="LevelAtAttempt"/> uses the caller's integer convention.
/// <see cref="ScaffoldingService.DecideNextScaffoldLevel"/> treats
/// HIGHER-int = MORE support (fade = -1, restore = +1, minLevel = floor).
/// The convenience constructor that accepts <see cref="ScaffoldingLevel"/>
/// inverts the enum (Full=0 → supportLevel=3) so callers can pass raw
/// scaffold-level enum values without hand-mapping.
/// </summary>
public readonly record struct AttemptOutcome(
    int LevelAtAttempt,
    bool IsCorrect)
{
    /// <summary>
    /// Convenience constructor that inverts a <see cref="ScaffoldingLevel"/>
    /// to the "higher-int = more support" convention used by
    /// <see cref="ScaffoldingService.DecideNextScaffoldLevel"/>.
    /// Full=0 → 3, Partial=1 → 2, HintsOnly=2 → 1, None=3 → 0.
    /// </summary>
    public AttemptOutcome(ScaffoldingLevel level, bool isCorrect)
        : this(3 - (int)level, isCorrect) { }
}

/// <summary>
/// Interface for scaffolding service operations.
/// Allows for testable, injectable access to scaffolding logic.
/// </summary>
public interface IScaffoldingService
{
    /// <summary>
    /// Determine scaffolding level from effective mastery and PSI.
    /// </summary>
    ScaffoldingLevel DetermineLevel(float effectiveMastery, float psi);

    /// <summary>
    /// Get metadata for LLM prompt construction from scaffolding level.
    /// </summary>
    ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level);
}

/// <summary>
/// Wrapper for the static ScaffoldingService to enable DI injection.
/// Stateless, thread-safe — registered as Singleton.
/// </summary>
public sealed class ScaffoldingServiceWrapper : IScaffoldingService
{
    public ScaffoldingLevel DetermineLevel(float effectiveMastery, float psi)
        => ScaffoldingService.DetermineLevel(effectiveMastery, psi);

    public ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level)
        => ScaffoldingService.GetScaffoldingMetadata(level);
}

/// <summary>
/// Determines how much instructional support the LLM provides
/// based on the student's effective mastery and prerequisite satisfaction.
/// Pure stateless mapping function.
/// </summary>
public static class ScaffoldingService
{
    /// <summary>
    /// Determine scaffolding level from effective mastery and PSI.
    /// </summary>
    public static ScaffoldingLevel DetermineLevel(float effectiveMastery, float psi)
    {
        if (effectiveMastery >= 0.70f)
            return ScaffoldingLevel.None;

        if (effectiveMastery < 0.20f && psi < 0.80f)
            return ScaffoldingLevel.Full;

        if (effectiveMastery < 0.40f)
            return ScaffoldingLevel.Partial;

        return ScaffoldingLevel.HintsOnly;
    }

    /// <summary>
    /// prr-041 worked-example fading hysteresis. Given the chronologically
    /// ordered attempt history AT THE CURRENT LEVEL (newer entries last) and
    /// the current numeric scaffold level, returns the next level the caller
    /// should apply. Semantics of the integer axis are caller-defined; this
    /// method treats "fade" as decrement (less support) and "restore" as
    /// increment (more support). The caller is free to map to
    /// <see cref="ScaffoldingLevel"/> ordinals in whatever direction matches
    /// its own policy.
    /// </summary>
    /// <param name="recentAttempts">
    /// Chronological attempts at <paramref name="currentLevel"/>. Only the
    /// tail that happened at <paramref name="currentLevel"/> is considered;
    /// attempts at other levels are ignored (caller may pass the full history).
    /// </param>
    /// <param name="currentLevel">Current numeric scaffold level.</param>
    /// <param name="minLevel">
    /// Floor. Caller guarantees <paramref name="currentLevel"/> &gt;=
    /// <paramref name="minLevel"/>; fading never goes below this.
    /// </param>
    /// <returns>Next scaffold level to apply. Convention: fade = currentLevel - 1, restore = currentLevel + 1, floor = minLevel.</returns>
    public static int DecideNextScaffoldLevel(
        IReadOnlyList<AttemptOutcome> recentAttempts,
        int currentLevel,
        int minLevel)
    {
        ArgumentNullException.ThrowIfNull(recentAttempts);

        // RESTORE rule: if the most recent attempt was at currentLevel and
        // was incorrect, AND the student arrived at currentLevel by fading
        // from a higher-support level (any prior attempt in history at
        // currentLevel+1 or higher), escalate one step back up.
        //
        // The "prior fade" check is the correct way to disambiguate "student
        // just got question wrong at their current level" (stay) from "we
        // faded them and they immediately stumbled" (restore). Without it,
        // every wrong answer would escalate scaffolding — the wrong shape
        // for a student who's correctly placed and just hit a hard item.
        if (recentAttempts.Count > 0)
        {
            var newest = recentAttempts[^1];
            if (!newest.IsCorrect && newest.LevelAtAttempt == currentLevel)
            {
                // Was the student previously at a HIGHER level (more
                // support) and faded down to currentLevel? Look for any
                // prior attempt with LevelAtAttempt > currentLevel (more
                // support in our convention: fade == -1, restore == +1).
                var wasFaded = false;
                for (var i = 0; i < recentAttempts.Count - 1; i++)
                {
                    if (recentAttempts[i].LevelAtAttempt > currentLevel)
                    {
                        wasFaded = true;
                        break;
                    }
                }
                if (wasFaded) return currentLevel + 1; // restore = +1 (back up to more support)
            }
        }

        // FADE rule. Walk the tail of attempts that happened at currentLevel
        // (newest → older). Count attempts at this level and track the
        // consecutive-correct streak measured from the newest attempt
        // backward. Stop counting consecutive-correct the moment we see an
        // incorrect; continue counting total attempts until the tail no
        // longer matches currentLevel.
        var attemptsAtLevel = 0;
        var consecutiveCorrect = 0;
        var streakAlive = true;
        var anyIncorrectAtLevel = false;

        for (var i = recentAttempts.Count - 1; i >= 0; i--)
        {
            var a = recentAttempts[i];
            if (a.LevelAtAttempt != currentLevel) break; // end of tail-at-level

            attemptsAtLevel++;
            if (!a.IsCorrect)
            {
                anyIncorrectAtLevel = true;
                streakAlive = false;
            }
            else if (streakAlive)
            {
                consecutiveCorrect++;
            }
        }

        // Minimum-attempts floor: need at least 2 attempts at this level
        // before we evaluate. Prevents snap decisions on a single answer.
        if (attemptsAtLevel < 2) return currentLevel;

        // Fade condition: 3 consecutive correct from newest backward AND no
        // incorrect in the counted window. Any incorrect at currentLevel
        // blocks fading — the hysteresis dead-zone.
        if (!anyIncorrectAtLevel && consecutiveCorrect >= 3)
        {
            var faded = currentLevel - 1;
            return faded < minLevel ? minLevel : faded;
        }

        // Hold: mixed record, too few consecutive correct, or floor reached.
        return currentLevel;
    }

    /// <summary>
    /// Get metadata for LLM prompt construction from scaffolding level.
    /// </summary>
    public static ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level) => level switch
    {
        ScaffoldingLevel.Full => new(level, "worked-example", ShowWorkedExample: true,
            ShowHintButton: true, MaxHints: 3, RevealAnswer: true),

        // RDY-013: Faded worked examples ARE the Partial-level technique
        // per Renkl & Atkinson (2003). ShowWorkedExample must be true so the
        // frontend receives the workedExample payload and renders it in faded mode.
        ScaffoldingLevel.Partial => new(level, "faded-example", ShowWorkedExample: true,
            ShowHintButton: true, MaxHints: 2, RevealAnswer: true),

        ScaffoldingLevel.HintsOnly => new(level, "hints-only", ShowWorkedExample: false,
            ShowHintButton: true, MaxHints: 1, RevealAnswer: false),

        ScaffoldingLevel.None => new(level, "independent", ShowWorkedExample: false,
            ShowHintButton: false, MaxHints: 0, RevealAnswer: false),

        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
