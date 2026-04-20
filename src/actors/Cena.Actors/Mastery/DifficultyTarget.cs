// =============================================================================
// Cena Platform — Cohort Difficulty Target (prr-041)
// 75% default; 85% inside 30-day pre-exam window; +5pp for Anxious profile.
// Past exam dates do NOT trigger the boost. See parallel BKT ADR for
// rationale (60% demoralizes IL Bagrut students per persona-educator review).
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// prr-041 cohort difficulty target. Returns the success-rate target the
/// scheduler + dashboards use to assess whether a student is on-pace.
/// Motivation-profile-aware + Bagrut-exam-date-aware.
/// </summary>
public static class DifficultyTarget
{
    /// <summary>Default cohort target — 75% (0.75).</summary>
    public const double Default = 0.75;

    /// <summary>Pre-exam confidence window target — 85% (0.85).</summary>
    public const double PreExam = 0.85;

    /// <summary>Anxious-profile boost — +5pp on whichever window applies.</summary>
    public const double AnxiousBoost = 0.05;

    /// <summary>Window in days before registered Bagrut exam when the pre-exam mode activates.</summary>
    public const int PreExamWindowDays = 30;

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
    {
        var baseTarget = IsInPreExamWindow(now, registeredBagrutDateUtc)
            ? PreExam
            : Default;

        var adjusted = profile == MotivationProfile.Anxious
            ? baseTarget + AnxiousBoost
            : baseTarget;

        // Guard against future motivation-profile values producing values
        // outside [0, 1]. +0.05 on 0.85 = 0.90 is fine; clamp is belt-and-
        // braces for callers who add a new profile with a larger boost.
        return Math.Clamp(adjusted, 0.0, 1.0);
    }

    private static bool IsInPreExamWindow(DateTimeOffset now, DateTimeOffset? registeredBagrutDateUtc)
    {
        if (!registeredBagrutDateUtc.HasValue) return false;
        var days = (registeredBagrutDateUtc.Value - now).TotalDays;
        return days >= 0 && days <= PreExamWindowDays;
    }
}
