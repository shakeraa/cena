// =============================================================================
// Cena Platform — Cohort Difficulty Target (prr-041, prr-030)
//
// 75% default; 85% inside 30-day pre-exam window; +5pp for Anxious profile.
// Past exam dates do NOT trigger the boost.
//
// prr-030 adds per-institute tuning: an institute can override the cohort
// default inside the Bjork-bounded range [0.6, 0.9] (see InstituteConfig).
//
// ─────────────────────────────────────────────────────────────────────────────
// WHY this target range exists (cite BEFORE you touch the numbers):
//
// Bjork & Bjork (2011) "Making things hard on yourself, but in a good way:
// Creating desirable difficulties to enhance learning" (Psychology and the
// Real World, pp.56–64) formalised the "desirable difficulty" principle —
// learning is optimised when retrieval is effortful but mostly successful,
// NOT when it is trivially easy (ceiling effect, no memory consolidation)
// NOR when it fails constantly (learned helplessness, surrender).
//
// Wilson, Shenhav, Straccia & Cohen (2019) "The Eighty Five Percent Rule
// for optimal learning" (Nature Communications 10:4646, DOI
// 10.1038/s41467-019-12552-4) derived 0.847 ≈ 85% as the theoretical
// optimum for *fixed-difficulty* stochastic gradient descent over binary
// classifiers. That is the mathematical ceiling; human learners with
// motivation budgets, metacognitive shame, and sleep-consolidation
// windows run BELOW it.
//
// The IL-Bagrut empirical range (persona-educator + Dr. Lior review from
// Israeli tutoring practice, audited 2026-04-20) lands at roughly 70–80%
// real-world success for Bagrut-prep cohorts. The 60% figure we shipped
// originally came from web-based general-purpose learning research —
// solid in its context, but a demoralising floor for a high-stakes
// national-exam audience where "I got 6/10 wrong" reads as failure, not
// as calibrated challenge. Raising the cohort default to 75% is the
// pre-exam-safe, demoralisation-safe floor; 85% applies inside the
// 30-day confidence-mode window where the goal flips from "push
// challenge" to "rehearse and reassure" (Bjork's desirable-difficulty
// principle does NOT apply under high-stakes exam-day conditions —
// Karpicke & Roediger 2008, DOI 10.1126/science.1152408, show that
// retrieval PRACTICE near an exam wants ceiling-near success).
//
// Empirical effect-size honesty (per ADR-0049 / prr-042):
//   - 85% rule (Wilson 2019): d≈0.4 on synthetic classifiers; NO human
//     RCT replication at the time of writing. This is a theoretical
//     upper bound, not a meta-analytic effect.
//   - Bjork desirable difficulty (umbrella): d≈0.3–0.5 across varied
//     interventions (spacing, interleaving, retrieval practice). Meta
//     analysis: Dunlosky et al. 2013, "Improving students' learning with
//     effective learning techniques", Psychological Science in the
//     Public Interest 14:4–58.
//   - IL-Bagrut cohort calibration: internal, not peer-reviewed. We do
//     NOT claim an RCT-grade effect size here — the 75% default is a
//     floor chosen from tutor-observed quit-rates at 60% vs 75%, not a
//     replicated experimental outcome.
//
// The clamp [0.6, 0.9] is the Bjork-bounded safe range:
//   - Below 0.6  → retrieval fails too often; extinction learning; quit.
//   - Above 0.9  → ceiling effect; no consolidation benefit from
//                  additional trials (Wilson 2019 derivation).
// An arch test (AccuracyTargetInBjorkRangeTest) enforces every
// config-declared target stays in this range.
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// prr-041 cohort difficulty target. Returns the success-rate target the
/// scheduler + dashboards use to assess whether a student is on-pace.
/// Motivation-profile-aware, Bagrut-exam-date-aware, and (prr-030)
/// institute-configurable within the Bjork-bounded range [0.6, 0.9].
/// </summary>
public static class DifficultyTarget
{
    /// <summary>
    /// Default cohort target — 75% (0.75). prr-030 raises from the
    /// originally-specified 60% (persona-educator review:
    /// demoralising for IL Bagrut cohorts) to a research-backed
    /// desirable-difficulty floor. See file header for citations.
    /// </summary>
    public const double Default = 0.75;

    /// <summary>Pre-exam confidence window target — 85% (0.85).</summary>
    public const double PreExam = 0.85;

    /// <summary>Anxious-profile boost — +5pp on whichever window applies.</summary>
    public const double AnxiousBoost = 0.05;

    /// <summary>Window in days before registered Bagrut exam when the pre-exam mode activates.</summary>
    public const int PreExamWindowDays = 30;

    /// <summary>
    /// Bjork-bounded minimum target. Below this, retrieval fails often
    /// enough to trigger extinction learning. prr-030 arch-test floor.
    /// </summary>
    public const double BjorkMinTarget = 0.60;

    /// <summary>
    /// Bjork-bounded maximum target. Above this, we hit the ceiling
    /// effect where additional trials add no consolidation signal
    /// (Wilson 2019 derivation).
    /// </summary>
    public const double BjorkMaxTarget = 0.90;

    /// <summary>
    /// Compute the cohort difficulty target as a success-rate fraction in
    /// [0.0, 1.0].
    /// </summary>
    /// <param name="profile">Student's self-reported motivation profile (RDY-057).</param>
    /// <param name="now">Reference timestamp (use <c>DateTimeOffset.UtcNow</c> in production).</param>
    /// <param name="registeredBagrutDateUtc">
    /// Student's registered Bagrut exam date, or <c>null</c> if none set.
    /// Only future dates inside the 30-day window trigger the pre-exam boost;
    /// past dates fall back to the default.
    /// </param>
    /// <returns>Target success rate in [0.0, 1.0].</returns>
    public static double TargetSuccessRate(
        MotivationProfile profile,
        DateTimeOffset now,
        DateTimeOffset? registeredBagrutDateUtc)
        => TargetSuccessRate(profile, now, registeredBagrutDateUtc, instituteTargetOverride: null);

    /// <summary>
    /// prr-030 overload: compute the cohort target with an optional
    /// per-institute override. When <paramref name="instituteTargetOverride"/>
    /// is supplied, it replaces <see cref="Default"/> as the cohort base;
    /// pre-exam window and anxious-boost still compose on top. The override
    /// value MUST sit in the Bjork-bounded range [0.6, 0.9] — values
    /// outside the range are rejected (ArgumentOutOfRangeException) rather
    /// than silently clamped, so misconfigured institutes fail loudly at
    /// startup instead of shipping a demoralising-floor session.
    /// </summary>
    public static double TargetSuccessRate(
        MotivationProfile profile,
        DateTimeOffset now,
        DateTimeOffset? registeredBagrutDateUtc,
        double? instituteTargetOverride)
    {
        if (instituteTargetOverride.HasValue)
        {
            var v = instituteTargetOverride.Value;
            if (v < BjorkMinTarget || v > BjorkMaxTarget)
                throw new ArgumentOutOfRangeException(
                    nameof(instituteTargetOverride),
                    v,
                    $"Institute target override must sit in the Bjork-bounded " +
                    $"range [{BjorkMinTarget:F2}, {BjorkMaxTarget:F2}]. " +
                    $"Below {BjorkMinTarget:F2} triggers extinction learning; " +
                    $"above {BjorkMaxTarget:F2} hits the ceiling effect.");
        }

        var cohortBase = instituteTargetOverride ?? Default;

        var baseTarget = IsInPreExamWindow(now, registeredBagrutDateUtc)
            ? PreExam
            : cohortBase;

        var adjusted = profile == MotivationProfile.Anxious
            ? baseTarget + AnxiousBoost
            : baseTarget;

        // Guard against future motivation-profile values producing values
        // outside [0, 1]. +0.05 on 0.85 = 0.90 is fine; clamp is belt-and-
        // braces for callers who add a new profile with a larger boost.
        return Math.Clamp(adjusted, 0.0, 1.0);
    }

    /// <summary>
    /// prr-030 arch-test helper. Returns true when the candidate target
    /// sits inside the Bjork-bounded range [0.6, 0.9]. Used by the
    /// AccuracyTargetInBjorkRange arch test to reject config-declared
    /// overrides that would ship a demoralising-floor or ceiling-effect
    /// session.
    /// </summary>
    public static bool IsInBjorkRange(double target)
        => target >= BjorkMinTarget && target <= BjorkMaxTarget;

    private static bool IsInPreExamWindow(DateTimeOffset now, DateTimeOffset? registeredBagrutDateUtc)
    {
        if (!registeredBagrutDateUtc.HasValue) return false;
        var days = (registeredBagrutDateUtc.Value - now).TotalDays;
        return days >= 0 && days <= PreExamWindowDays;
    }
}
