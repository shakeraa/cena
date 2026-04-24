// =============================================================================
// Cena Platform — Theta-to-Mastery Mapper (RDY-023)
//
// Maps IRT theta (ability estimate) to BKT P_Initial (initial mastery).
// Uses the logistic (sigmoid) function so the full IRT scale [-∞, +∞]
// maps smoothly to (0, 1) mastery probability space.
//
// Typical theta range: [-3, +3] for K-12 adaptive learning.
//   theta = -3 → P_Initial ≈ 0.05 (novice)
//   theta =  0 → P_Initial = 0.50 (average)
//   theta = +3 → P_Initial ≈ 0.95 (near-mastery)
// =============================================================================

using System.Runtime.CompilerServices;
using Cena.Actors.Mastery;

namespace Cena.Actors.Services;

public static class ThetaMasteryMapper
{
    // Clamp bounds match BktService (MinP/MaxP)
    private const double MinMastery = 0.01;
    private const double MaxMastery = 0.99;

    /// <summary>
    /// Default P_Initial when the student skips the diagnostic quiz.
    /// Matches <see cref="BktParameters.Default"/>.PInitial.
    /// </summary>
    public const double SkipDefault = 0.10;

    /// <summary>
    /// Map IRT theta (ability) to BKT P_Initial (initial mastery).
    /// Uses the standard logistic function: σ(θ) = 1 / (1 + e^(−θ)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ThetaToPInitial(double theta)
    {
        var raw = 1.0 / (1.0 + Math.Exp(-theta));
        return Math.Clamp(raw, MinMastery, MaxMastery);
    }

    /// <summary>
    /// Map a batch of per-subject theta estimates to per-subject P_Initial values.
    /// </summary>
    public static Dictionary<string, double> MapAll(
        IEnumerable<(string Subject, double Theta)> estimates)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (subject, theta) in estimates)
            result[subject] = ThetaToPInitial(theta);
        return result;
    }

    // =========================================================================
    // prr-007 ReadinessBucket seam (single authorized constructor of the ordinal).
    // Per ADR-0012 SessionRiskAssessment pattern + RDY-080 prediction-surface ban,
    // raw IRT theta scalars MUST NOT reach student/teacher/parent DTOs. This seam
    // is the only legitimate path; arch test NoThetaInOutboundDtoTest enforces.
    //
    // Bucket boundaries (point-estimate):
    //   θ < -1.0   → Emerging
    //   θ ∈ [-1.0, 0.0) → Developing
    //   θ ∈ [0.0, 1.0)  → Proficient
    //   θ ≥ 1.0    → ExamReady
    //
    // CI-aware down-rounding: when [θ - CI, θ + CI] crosses a bucket boundary,
    // the LOWER bucket wins (Ministry-defensibility — a conservative readiness
    // claim is always preferable to an optimistic one). Non-finite θ and
    // extremely wide CIs fall to Emerging.
    // =========================================================================

    private const double BucketBoundaryEmergingDeveloping = -1.0;
    private const double BucketBoundaryDevelopingProficient = 0.0;
    private const double BucketBoundaryProficientExamReady = 1.0;

    /// <summary>
    /// Convert an IRT theta + confidence-interval half-width into an ordinal
    /// readiness bucket suitable for student/teacher/parent-visible DTOs.
    /// CI-straddle of a boundary down-rounds to the lower bucket.
    /// </summary>
    public static ReadinessBucket ToReadinessBucket(double theta, double confidenceIntervalHalfWidth)
    {
        // Non-finite theta → conservative Emerging
        if (double.IsNaN(theta) || double.IsInfinity(theta))
            return ReadinessBucket.Emerging;

        // Negative CI is nonsense → treat as zero (pure point bucket)
        var ci = confidenceIntervalHalfWidth > 0.0 ? confidenceIntervalHalfWidth : 0.0;

        // CI-straddle rule: bucket = BucketAt(lowerBound) when lower and upper
        // disagree. When CI is 0, lowerBound == theta, so this degenerates to
        // the pure point-bucket lookup.
        var lowerBound = theta - ci;
        var upperBound = theta + ci;

        var lowerBucket = BucketAt(lowerBound);
        var upperBucket = BucketAt(upperBound);

        // Same bucket → point case. Different buckets → down-round to lower.
        return lowerBucket == upperBucket ? lowerBucket : lowerBucket;
    }

    private static ReadinessBucket BucketAt(double theta)
    {
        if (theta < BucketBoundaryEmergingDeveloping) return ReadinessBucket.Emerging;
        if (theta < BucketBoundaryDevelopingProficient) return ReadinessBucket.Developing;
        if (theta < BucketBoundaryProficientExamReady) return ReadinessBucket.Proficient;
        return ReadinessBucket.ExamReady;
    }
}
