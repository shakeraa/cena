// =============================================================================
// Cena Platform -- BKT Parameters
// MST-002: Bayesian Knowledge Tracing parameter set per knowledge component
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// BKT (Bayesian Knowledge Tracing) parameters for a knowledge component.
/// P_L0 = prior probability of knowing, P_T = learning transition,
/// P_S = slip probability, P_G = guess probability.
/// </summary>
public readonly record struct BktParameters(float P_L0, float P_T, float P_S, float P_G)
{
    /// <summary>Default parameters for launch (before trainer calibrates).</summary>
    public static readonly BktParameters Default = new(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);

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
