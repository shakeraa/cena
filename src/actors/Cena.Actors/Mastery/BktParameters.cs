// =============================================================================
// BktParameters — parameter policy per ADR-0039 (Koedinger defaults).
// Per-student parameter learning is forbidden. Changes require new ADR.
// Enforced by `BktParametersLockedTest` in Cena.Actors.Tests/Architecture/.
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// BKT (Bayesian Knowledge Tracing) parameters for a knowledge component.
/// P_L0 = prior probability of knowing, P_T = learning transition,
/// P_S = slip probability, P_G = guess probability.
/// </summary>
public readonly record struct BktParameters(float P_L0, float P_T, float P_S, float P_G)
{
    // Per ADR-0039 — locked Koedinger literature defaults. Any change requires a new ADR.
    /// <summary>Initial mastery probability at first exposure (Koedinger default).</summary>
    public const double PInit = 0.3;

    /// <summary>Probability of transitioning unmastered → mastered per correct attempt (Koedinger default).</summary>
    public const double PLearn = 0.15;

    /// <summary>Probability of incorrect response despite mastery (Koedinger default).</summary>
    public const double PSlip = 0.10;

    /// <summary>Probability of correct response without mastery (Koedinger default).</summary>
    public const double PGuess = 0.15;

    /// <summary>
    /// Sibling constant to ADR-0039's Koedinger set: within-session micro-forgetting
    /// factor applied by <c>BktService</c> after each trial update. Not part of the
    /// Koedinger paper — it's a Cena-specific dampener that prevents mastery from
    /// ratcheting up on lucky-guess streaks. Long-term decay is HLR's responsibility
    /// and is orthogonal to this constant. Follow-up doc edit will append a §PForget
    /// section to ADR-0039 to document the deviation; the constant lives here so
    /// every BKT consumer routes through a single source of truth.
    /// </summary>
    public const double PForget = 0.02;

    // Per ADR-0039 — Default uses the locked Koedinger constants above (cast to float for struct storage).
    /// <summary>Default parameters locked to Koedinger literature defaults per ADR-0039.</summary>
    public static readonly BktParameters Default = new(
        P_L0: (float)PInit,
        P_T: (float)PLearn,
        P_S: (float)PSlip,
        P_G: (float)PGuess);

    /// <summary>
    /// Validates identifiability constraint: P_S + P_G must be less than 1.
    /// All parameters must be in [0, 1].
    /// </summary>
    public bool IsValid =>
        P_L0 is >= 0f and <= 1f &&
        P_T is >= 0f and <= 1f &&
        P_S is >= 0f and <= 1f &&
        P_G is >= 0f and <= 1f &&
        P_S + P_G < 1f;

    /// <summary>
    /// SAI-002: Reduce P_T (learning transition probability) based on hints used.
    /// More hints = less credit for a correct answer. The student demonstrated less
    /// independent mastery, so the learning transition is attenuated.
    /// Credit curve: 0 hints = 1.0x, 1 = 0.7x, 2 = 0.4x, 3+ = 0.1x.
    /// </summary>
    public static BktParameters AdjustForHints(BktParameters baseParams, int hintsUsed)
    {
        float creditMultiplier = hintsUsed switch
        {
            0 => 1.0f,
            1 => 0.7f,
            2 => 0.4f,
            _ => 0.1f
        };
        return baseParams with { P_T = baseParams.P_T * creditMultiplier };
    }
}
