// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Half-Life Regression (HLR) Service
// Layer: Domain Services | Runtime: .NET 9
// Reference: Settles & Meeder (2016) — A Trainable Spaced Repetition Model
//
// Core formula: p(t) = 2^(-delta/h)
//   where delta = time since last review, h = half-life
// ═══════════════════════════════════════════════════════════════════════

using System.Runtime.CompilerServices;

namespace Cena.Actors.Services;

public interface IHlrService
{
    /// <summary>
    /// Compute predicted recall probability at time delta after last review.
    /// p(t) = 2^(-delta/h)
    /// </summary>
    /// <param name="halfLifeHours">Current half-life in hours (h > 0).</param>
    /// <param name="hoursSinceReview">Hours elapsed since last review (delta >= 0).</param>
    /// <returns>Recall probability in [0, 1].</returns>
    double ComputeRecall(double halfLifeHours, double hoursSinceReview);

    /// <summary>
    /// Compute the time until recall decays to the given threshold.
    /// Solving: threshold = 2^(-t/h) → t = -h * log2(threshold)
    /// </summary>
    /// <param name="halfLifeHours">Current half-life in hours.</param>
    /// <param name="threshold">Recall threshold (default <see cref="MasteryConstants.RecallReviewThreshold"/>).</param>
    /// <returns>Time until recall drops to threshold.</returns>
    TimeSpan ComputeTimeToThreshold(double halfLifeHours, double threshold = MasteryConstants.RecallReviewThreshold);

    /// <summary>
    /// Update half-life after a review event.
    /// Correct answers increase the half-life (stronger memory);
    /// incorrect answers decrease it (weaker memory).
    /// Response time modulates the update: faster correct = stronger signal.
    /// </summary>
    /// <param name="previousHalfLife">Previous half-life in hours.</param>
    /// <param name="wasCorrect">Whether the review response was correct.</param>
    /// <param name="responseTimeMs">Response time in milliseconds.</param>
    /// <returns>Updated half-life in hours.</returns>
    double UpdateHalfLife(double previousHalfLife, bool wasCorrect, int responseTimeMs);
}

public sealed class HlrService : IHlrService
{
    // Minimum half-life: 1 hour (prevents degenerate scheduling)
    private const double MinHalfLife = 1.0;

    // Maximum half-life: ~6 months — beyond this, concept is deeply mastered
    private const double MaxHalfLife = 4320.0;

    // Correct answer scaling factor: half-life multiplied by this on success
    private const double CorrectGrowthBase = 2.0;

    // Incorrect answer decay factor: half-life multiplied by this on failure
    private const double IncorrectDecayFactor = 0.5;

    // Response time reference point (median expected time in ms)
    private const double ReferenceResponseTimeMs = 5000.0;

    // How much response time modulates the update (0 = no effect, 1 = full effect)
    private const double ResponseTimeWeight = 0.3;

    private static readonly double Log2E = 1.0 / Math.Log(2.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ComputeRecall(double halfLifeHours, double hoursSinceReview)
    {
        if (halfLifeHours <= 0.0)
            return 0.0;

        if (hoursSinceReview <= 0.0)
            return 1.0;

        // p(t) = 2^(-delta/h) = e^(-delta/h * ln(2))
        double exponent = -hoursSinceReview / halfLifeHours;
        return Math.Pow(2.0, exponent);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeSpan ComputeTimeToThreshold(double halfLifeHours, double threshold = MasteryConstants.RecallReviewThreshold)
    {
        if (halfLifeHours <= 0.0)
            return TimeSpan.Zero;

        // Clamp threshold to valid range
        if (threshold <= 0.0 || threshold >= 1.0)
            threshold = MasteryConstants.RecallReviewThreshold;

        // Solve: threshold = 2^(-t/h)
        // log2(threshold) = -t/h
        // t = -h * log2(threshold)
        double log2Threshold = Math.Log(threshold) * Log2E;
        double hours = -halfLifeHours * log2Threshold;

        if (hours < 0.0)
            return TimeSpan.Zero;

        return TimeSpan.FromHours(hours);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double UpdateHalfLife(double previousHalfLife, bool wasCorrect, int responseTimeMs)
    {
        if (previousHalfLife < MinHalfLife)
            previousHalfLife = MinHalfLife;

        // Compute response time modulator:
        // Fast correct answers → stronger memory signal → bigger half-life increase
        // Slow correct answers → weaker signal → smaller increase
        // Fast incorrect answers → careless error → moderate decrease
        // Slow incorrect answers → genuine confusion → larger decrease
        double rtRatio = responseTimeMs > 0
            ? ReferenceResponseTimeMs / responseTimeMs
            : 1.0;

        // Clamp ratio to prevent extreme modulation
        if (rtRatio < 0.5) rtRatio = 0.5;
        if (rtRatio > 2.0) rtRatio = 2.0;

        double newHalfLife;

        if (wasCorrect)
        {
            // Half-life grows: faster correct = larger growth
            // responseTimeModulator > 1 when fast, < 1 when slow
            double responseTimeModulator = 1.0 + ResponseTimeWeight * (rtRatio - 1.0);
            newHalfLife = previousHalfLife * CorrectGrowthBase * responseTimeModulator;
        }
        else
        {
            // Half-life shrinks: slow incorrect = larger shrink
            // Invert the modulator: slow answers shrink more
            double responseTimeModulator = 1.0 + ResponseTimeWeight * (1.0 / rtRatio - 1.0);
            newHalfLife = previousHalfLife * IncorrectDecayFactor * responseTimeModulator;
        }

        // Clamp to valid range
        if (newHalfLife < MinHalfLife) return MinHalfLife;
        if (newHalfLife > MaxHalfLife) return MaxHalfLife;
        return newHalfLife;
    }
}
