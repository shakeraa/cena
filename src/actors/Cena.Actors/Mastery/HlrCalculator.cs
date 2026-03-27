// =============================================================================
// Cena Platform -- HLR Calculator
// MST-003: Half-Life Regression computation (Settles & Meeder 2016)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Half-Life Regression calculator. Predicts when a student will forget a concept.
/// All methods are static, zero allocation.
/// </summary>
public static class HlrCalculator
{
    private const float MinHalfLifeHours = 1.0f;
    private const float MaxHalfLifeHours = 8760.0f; // 1 year

    /// <summary>
    /// Compute the memory half-life in hours: h = 2^(theta dot x + bias).
    /// Clamped to [1 hour, 1 year].
    /// </summary>
    public static float ComputeHalfLife(HlrFeatures features, HlrWeights weights)
    {
        float exponent = weights.DotProduct(features);
        float halfLife = (float)Math.Pow(2.0, exponent);
        return Math.Clamp(halfLife, MinHalfLifeHours, MaxHalfLifeHours);
    }

    /// <summary>
    /// Compute recall probability: p(t) = 2^(-elapsed / halfLife).
    /// Returns 0 if halfLife is invalid, 1.0 if just practiced.
    /// </summary>
    public static float ComputeRecall(float halfLifeHours, TimeSpan elapsed)
    {
        if (halfLifeHours <= 0f)
            return 0f;
        if (elapsed <= TimeSpan.Zero)
            return 1.0f;

        return (float)Math.Pow(2.0, -elapsed.TotalHours / halfLifeHours);
    }

    /// <summary>
    /// Schedule next review: time until recall drops to threshold.
    /// Formula: t = -h * log2(threshold).
    /// With threshold=0.85: review when recall drops to 85%.
    /// </summary>
    public static TimeSpan ScheduleNextReview(float halfLifeHours, float threshold = MasteryConstants.RecallReviewThresholdF)
    {
        if (halfLifeHours <= 0f || threshold <= 0f || threshold >= 1f)
            return TimeSpan.Zero;

        double hours = -halfLifeHours * Math.Log2(threshold);
        return TimeSpan.FromHours(hours);
    }

    /// <summary>
    /// Updates a ConceptMasteryState with a new half-life computed from features.
    /// Only modifies HalfLifeHours.
    /// </summary>
    public static ConceptMasteryState UpdateState(
        ConceptMasteryState state, HlrFeatures features, HlrWeights weights)
    {
        float newHalfLife = ComputeHalfLife(features, weights);
        return state.WithHalfLifeUpdate(newHalfLife);
    }
}
