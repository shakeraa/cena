// =============================================================================
// prr-041 worked-example fading hysteresis — ScaffoldingService.DecideNextScaffoldLevel
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class ScaffoldingHysteresisTests
{
    // Convention in these tests: HIGHER int = MORE support.
    // Fade = -1. Restore = +1. minLevel = floor (least-support cap).
    private const int L = 3;     // "current" (e.g. full worked example)
    private const int LMinus1 = 2; // one step less support
    private const int Floor = 0;  // least support permitted

    private static AttemptOutcome At(int level, bool correct) => new(level, correct);

    [Fact]
    public void Stays_at_L_after_first_correct_until_minimum_attempts_met()
    {
        // One correct answer is below the 2-attempts floor → stay.
        var history = new[] { At(L, true) };
        Assert.Equal(L, ScaffoldingService.DecideNextScaffoldLevel(history, L, Floor));
    }

    [Fact]
    public void Stays_at_L_after_two_correct_not_yet_three_consecutive()
    {
        // Two correct at L — min-attempts met but streak < 3 → stay.
        var history = new[] { At(L, true), At(L, true) };
        Assert.Equal(L, ScaffoldingService.DecideNextScaffoldLevel(history, L, Floor));
    }

    [Fact]
    public void Fades_L_to_LMinus1_after_three_consecutive_correct()
    {
        var history = new[] { At(L, true), At(L, true), At(L, true) };
        Assert.Equal(LMinus1, ScaffoldingService.DecideNextScaffoldLevel(history, L, Floor));
    }

    [Fact]
    public void Restores_LMinus1_to_L_when_incorrect_after_prior_fade()
    {
        // Student faded to L-1 after 3 correct at L, then got one wrong at L-1.
        var history = new[] { At(L, true), At(L, true), At(L, true), At(LMinus1, false) };
        Assert.Equal(L, ScaffoldingService.DecideNextScaffoldLevel(history, LMinus1, Floor));
    }

    [Fact]
    public void Mixed_two_correct_plus_one_incorrect_at_L_stays_at_L()
    {
        // Min-attempts met (3 attempts) but the streak is broken by the
        // incorrect → no fade. No prior fade, so no restore either.
        var history = new[] { At(L, true), At(L, true), At(L, false) };
        Assert.Equal(L, ScaffoldingService.DecideNextScaffoldLevel(history, L, Floor));
    }

    [Fact]
    public void Does_not_fade_below_minLevel()
    {
        // At the floor with 3 consecutive correct — cap at minLevel.
        var history = new[] { At(Floor, true), At(Floor, true), At(Floor, true) };
        Assert.Equal(Floor, ScaffoldingService.DecideNextScaffoldLevel(history, Floor, Floor));
    }

    [Fact]
    public void Single_incorrect_at_L_without_prior_fade_does_not_escalate()
    {
        // One wrong answer at L with no prior higher-support history →
        // hold (don't escalate above the student's declared currentLevel).
        var history = new[] { At(L, true), At(L, false) };
        Assert.Equal(L, ScaffoldingService.DecideNextScaffoldLevel(history, L, Floor));
    }

    [Fact]
    public void Attempts_at_other_levels_do_not_count_toward_fade_streak()
    {
        // Two correct at L-1 + one correct at L should NOT trigger a fade
        // from L (tail-at-level is only 1 attempt).
        var history = new[] { At(LMinus1, true), At(LMinus1, true), At(L, true) };
        Assert.Equal(L, ScaffoldingService.DecideNextScaffoldLevel(history, L, Floor));
    }
}
