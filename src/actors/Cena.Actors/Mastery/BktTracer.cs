// =============================================================================
// Cena Platform -- BKT Tracer
// MST-002: Bayesian Knowledge Tracing update rule (hot path, zero allocation)
// PRR-261: discount-weighted variant for reference-anchored attempts
//          (Sweller 1998 worked-example transient, ADR-0059 §15.9 R12).
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
    public static float Update(float currentP_L, bool isCorrect, BktParameters p) =>
        UpdateWithDiscount(currentP_L, isCorrect, p, discountFactor: 1f);

    /// <summary>
    /// Discount-weighted BKT update for reference-anchored attempts (PRR-261,
    /// ADR-0059 §15.9 R12). The observation is folded in at <paramref name="discountFactor"/>
    /// strength: 1.0 = full standard update (identical to <see cref="Update"/>),
    /// 0.5 = half-weight (the §15.9 default for attempts within 5 minutes of a
    /// reference render — Sweller worked-example transient), 0.0 = no observation
    /// influence (only the learning transition advances P(L)).
    /// </summary>
    /// <remarks>
    /// Math: compute the standard Bayesian posterior given the observation,
    /// then linearly interpolate between the prior and that posterior using
    /// <paramref name="discountFactor"/> as the weight on the observation.
    /// Then apply the learning transition exactly as in <see cref="Update"/>.
    /// This preserves the property that <c>discountFactor=1</c> is bit-identical
    /// to the unweighted call, so non-anchored attempts are unaffected.
    /// </remarks>
    /// <param name="currentP_L">Prior P(L) ∈ [0, 1].</param>
    /// <param name="isCorrect">Observation: did the student succeed?</param>
    /// <param name="p">BKT parameters for this skill.</param>
    /// <param name="discountFactor">
    /// Observation weight ∈ [0, 1]. 1 = full update, 0.5 = half-weight,
    /// 0 = ignore observation. Out-of-range values are clamped.
    /// </param>
    public static float UpdateWithDiscount(
        float currentP_L,
        bool isCorrect,
        BktParameters p,
        float discountFactor)
    {
        // Step 1: Compute posterior P(L | observation) — full Bayesian update.
        float posteriorFull;
        if (isCorrect)
        {
            // P(L|correct) = (1-P_S) * P_L / [(1-P_S)*P_L + P_G*(1-P_L)]
            float numerator = (1f - p.P_S) * currentP_L;
            float denominator = numerator + p.P_G * (1f - currentP_L);
            posteriorFull = denominator > 0f ? numerator / denominator : currentP_L;
        }
        else
        {
            // P(L|incorrect) = P_S * P_L / [P_S*P_L + (1-P_G)*(1-P_L)]
            float numerator = p.P_S * currentP_L;
            float denominator = numerator + (1f - p.P_G) * (1f - currentP_L);
            posteriorFull = denominator > 0f ? numerator / denominator : currentP_L;
        }

        // Step 1.5: Discount-weighted blend between prior and full posterior.
        // Fast-path the common case (factor=1) to preserve BIT-IDENTICAL
        // output with the legacy Update() method — `prior + 1*(post - prior)`
        // can drift by a ULP due to floating-point non-associativity, and
        // downstream selectors (QuestionSelector probability bands, etc.)
        // are tight enough to flake on that.
        float w = Math.Clamp(discountFactor, 0f, 1f);
        float posterior = w >= 1f
            ? posteriorFull
            : currentP_L + w * (posteriorFull - currentP_L);

        // Step 2: Learning transition — applied to the discounted posterior.
        // P(L_next) = P(L|obs') + (1 - P(L|obs')) * P_T
        float next = posterior + (1f - posterior) * p.P_T;

        // Clamp to prevent numerical issues.
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
