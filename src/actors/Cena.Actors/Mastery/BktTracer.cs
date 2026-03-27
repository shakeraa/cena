// =============================================================================
// Cena Platform -- BKT Tracer
// MST-002: Bayesian Knowledge Tracing update rule (hot path, zero allocation)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Bayesian Knowledge Tracing update engine. Pure static math, zero allocation.
/// Runs on every ConceptAttempted event inside the StudentActor.
/// </summary>
public static class BktTracer
{
    /// <summary>
    /// Core BKT update: given current P(L), observation, and parameters, compute new P(L).
    /// Executes in &lt; 1 microsecond, zero heap allocation.
    /// </summary>
    public static float Update(float currentP_L, bool isCorrect, BktParameters p)
    {
        // Step 1: Compute posterior P(L | observation)
        float posterior;
        if (isCorrect)
        {
            // P(L|correct) = (1-P_S) * P_L / [(1-P_S)*P_L + P_G*(1-P_L)]
            float numerator = (1f - p.P_S) * currentP_L;
            float denominator = numerator + p.P_G * (1f - currentP_L);
            posterior = denominator > 0f ? numerator / denominator : currentP_L;
        }
        else
        {
            // P(L|incorrect) = P_S * P_L / [P_S*P_L + (1-P_G)*(1-P_L)]
            float numerator = p.P_S * currentP_L;
            float denominator = numerator + (1f - p.P_G) * (1f - currentP_L);
            posterior = denominator > 0f ? numerator / denominator : currentP_L;
        }

        // Step 2: Learning transition
        // P(L_next) = P(L|obs) + (1 - P(L|obs)) * P_T
        float next = posterior + (1f - posterior) * p.P_T;

        // Clamp to prevent numerical issues
        return Math.Clamp(next, 0.001f, 0.999f);
    }

    /// <summary>
    /// Updates a ConceptMasteryState with a new BKT probability.
    /// Only modifies MasteryProbability — other fields have their own updaters.
    /// </summary>
    public static ConceptMasteryState UpdateState(
        ConceptMasteryState state, bool isCorrect, BktParameters p)
    {
        float newProbability = Update(state.MasteryProbability, isCorrect, p);
        return state.WithBktUpdate(newProbability);
    }
}
