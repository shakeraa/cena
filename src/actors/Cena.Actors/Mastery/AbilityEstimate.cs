// =============================================================================
// Cena Platform — Ability Estimate (RDY-071 Phase 1A)
//
// A student's IRT ability θ at a point in time, with its standard error
// and supporting-evidence metadata. This is the primary signal for the
// mastery-trajectory view: the student sees a HIGH / MEDIUM / LOW bucket
// derived from θ, never a numeric forward-extrapolation score — that
// view is gated by RDY-080 concordance-mapping approval.
//
// See:
//   - ADR-0032 (IRT 2PL calibration)
//   - docs/psychometrics/calibration-study-design.md (RDY-080) —
//     why θ → Bagrut scaled-score is blocked until calibrated
//   - docs/engineering/mastery-trajectory-honest-framing.md (this
//     commit) — the banned-phrase list enforced by shipgate
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Qualitative mastery bucket shown to students and parents. Derived
/// from θ with an explicit 80% CI check — never a numeric bagrut score
/// (that view is gated on RDY-080 concordance mapping approval).
/// </summary>
public enum MasteryBucket
{
    /// <summary>Insufficient evidence — show "keep practicing" copy, no bucket claim.</summary>
    Inconclusive = 0,

    /// <summary>Lower quartile of the ability scale, 80% CI contained in the low band.</summary>
    Low = 1,

    /// <summary>Middle range, 80% CI contained in the medium band.</summary>
    Medium = 2,

    /// <summary>Upper quartile, 80% CI contained in the high band.</summary>
    High = 3
}

/// <summary>
/// One ability-estimate snapshot for one student on one topic (or
/// overall composite). Immutable value record; trajectory views take a
/// time-ordered list of these.
/// </summary>
public sealed record AbilityEstimate(
    string StudentAnonId,
    string TopicSlug,
    double Theta,
    double StandardError,
    int SampleSize,
    DateTimeOffset ComputedAtUtc,
    int ObservationWindowWeeks)
{
    /// <summary>
    /// 80% confidence interval half-width (≈ 1.282 × SE under a normal
    /// approximation — the standard textbook value). Exposed as a
    /// computed property so callers can show "±X" alongside the point
    /// without re-computing it per render.
    /// </summary>
    public double ConfidenceInterval80 => 1.2816 * StandardError;

    /// <summary>Lower bound of the 80% CI on the θ scale.</summary>
    public double LowerBound80 => Theta - ConfidenceInterval80;

    /// <summary>Upper bound of the 80% CI on the θ scale.</summary>
    public double UpperBound80 => Theta + ConfidenceInterval80;

    /// <summary>
    /// Classify into a mastery bucket with an explicit minimum-evidence
    /// guard. Returns <see cref="MasteryBucket.Inconclusive"/> when:
    ///  * SampleSize below the minimum threshold (default 30 problems), OR
    ///  * The 80% CI straddles two buckets — the signal is too weak to
    ///    commit to one bucket honestly.
    ///
    /// Bucket boundaries are pre-registered on the z-scored θ scale:
    ///   θ &lt; -0.5  → Low
    ///   -0.5 ≤ θ ≤ +0.5 → Medium
    ///   θ &gt; +0.5  → High
    /// Shifting the boundaries requires Dr. Yael sign-off per the
    /// RDY-071 honest-framing contract; the constants here are the
    /// shipping defaults.
    /// </summary>
    public MasteryBucket Bucket(int minimumSampleSize = 30)
    {
        if (SampleSize < minimumSampleSize) return MasteryBucket.Inconclusive;

        const double lowUpper = -0.5;
        const double highLower = 0.5;

        // Lower + upper bounds must both sit in the same band, else the
        // 80% CI crosses a boundary and we refuse to commit.
        if (UpperBound80 < lowUpper) return MasteryBucket.Low;
        if (LowerBound80 > highLower) return MasteryBucket.High;
        if (LowerBound80 >= lowUpper && UpperBound80 <= highLower)
            return MasteryBucket.Medium;

        return MasteryBucket.Inconclusive;
    }
}
