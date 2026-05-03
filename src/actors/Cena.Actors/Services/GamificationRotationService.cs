// =============================================================================
// Cena Platform — Gamification Novelty Rotation Engine (FOC-011)
//
// Meta-analysis (Zeng et al., 2024): gamification has large overall effect
// (g = 0.822) BUT interventions >1 semester have negligible/negative effect.
// This service rotates game elements to maintain engagement freshness.
//
// FOC-011.1: Rotation engine — rotates primary/secondary elements per student
// FOC-011.2: Engagement decay tracking — detects staleness per element
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Determines which gamification elements should be prominent for a student
/// based on their tenure with each element and observed engagement decay.
/// </summary>
public interface IGamificationRotationService
{
    /// <summary>
    /// Computes the current element rotation for a student.
    /// </summary>
    GamificationRotation ComputeRotation(GamificationProfile profile);

    /// <summary>
    /// Checks if an element's engagement has decayed enough to trigger rotation.
    /// </summary>
    bool ShouldRotate(ElementEngagement engagement);

    /// <summary>
    /// Returns the streak consistency weight adjusted for student tenure.
    /// FOC-011.3: weight reduces over time as novelty fades.
    /// </summary>
    double GetStreakWeight(int studentTenureDays);
}

public sealed class GamificationRotationService : IGamificationRotationService
{
    private readonly ILogger<GamificationRotationService> _logger;

    // Rotation thresholds
    private const int IntroNewElementDays = 30;
    private const int FullRotationDays = 90;
    private const double EngagementDecayThreshold = 0.30; // 30% drop triggers rotation
    private const int EngagementDecayWindowDays = 14;
    private const int DurableElementDays = 60;

    public GamificationRotationService(ILogger<GamificationRotationService> logger)
    {
        _logger = logger;
    }

    public GamificationRotation ComputeRotation(GamificationProfile profile)
    {
        var primary = profile.CurrentPrimary;
        var secondary = profile.CurrentSecondary;
        var pool = profile.AvailableElements;
        var daysSincePrimaryAssigned = (DateTimeOffset.UtcNow - profile.PrimaryAssignedAt).TotalDays;

        // After 90 days: full rotation — swap primary with least-used pool element
        if (daysSincePrimaryAssigned >= FullRotationDays)
        {
            var nextPrimary = SelectFreshestElement(pool, profile.ElementEngagements, primary, secondary);
            _logger.LogInformation(
                "FOC-011: Full rotation for {StudentId}: {Old} → {New} (after {Days}d)",
                profile.StudentId, primary, nextPrimary, (int)daysSincePrimaryAssigned);

            return new GamificationRotation(
                Primary: nextPrimary,
                Secondary: primary, // demote old primary to secondary
                Action: RotationAction.FullRotation,
                Reason: $"Primary element '{primary}' exceeded {FullRotationDays}-day threshold");
        }

        // After 30 days: introduce new secondary alongside existing primary
        if (daysSincePrimaryAssigned >= IntroNewElementDays && secondary == profile.OriginalSecondary)
        {
            var newSecondary = SelectFreshestElement(pool, profile.ElementEngagements, primary, secondary);
            if (newSecondary != secondary)
            {
                _logger.LogInformation(
                    "FOC-011: Introducing new secondary for {StudentId}: {Old} → {New}",
                    profile.StudentId, secondary, newSecondary);

                return new GamificationRotation(
                    Primary: primary,
                    Secondary: newSecondary,
                    Action: RotationAction.IntroduceNewSecondary,
                    Reason: $"Introducing fresh secondary after {IntroNewElementDays} days");
            }
        }

        // Check engagement decay on primary — force early rotation if needed
        var primaryEngagement = profile.ElementEngagements
            .FirstOrDefault(e => e.Element == primary);

        if (primaryEngagement is not null && ShouldRotate(primaryEngagement))
        {
            var replacement = SelectFreshestElement(pool, profile.ElementEngagements, primary, secondary);
            _logger.LogInformation(
                "FOC-011: Engagement decay rotation for {StudentId}: {Old} → {New} (decay: {Decay:P0})",
                profile.StudentId, primary, replacement, primaryEngagement.EngagementDecayRate);

            return new GamificationRotation(
                Primary: replacement,
                Secondary: primary,
                Action: RotationAction.EngagementDecayRotation,
                Reason: $"Engagement with '{primary}' dropped {primaryEngagement.EngagementDecayRate:P0} over {EngagementDecayWindowDays} days");
        }

        // No rotation needed
        return new GamificationRotation(
            Primary: primary,
            Secondary: secondary,
            Action: RotationAction.NoChange,
            Reason: "Engagement stable");
    }

    public bool ShouldRotate(ElementEngagement engagement)
    {
        // If engagement rate dropped >30% over 2 weeks, flag for rotation
        if (engagement.EngagementDecayRate > EngagementDecayThreshold)
            return true;

        // But if engagement has been stable >60 days, it's durable — don't rotate
        if (engagement.DaysStable >= DurableElementDays)
            return false;

        return false;
    }

    public double GetStreakWeight(int studentTenureDays)
    {
        // FOC-011.3: Streak consistency weight fades with tenure
        // Month 1: 0.15 (full weight)
        // Month 3+: 0.10 (reduced)
        // Month 6+: 0.05 (minimal — streak novelty has worn off)
        return studentTenureDays switch
        {
            < 90 => 0.15,
            < 180 => 0.10,
            _ => 0.05
        };
    }

    /// <summary>
    /// Selects the element from the pool that has been used least recently,
    /// excluding the current primary and secondary.
    /// </summary>
    private static GameElement SelectFreshestElement(
        IReadOnlyList<GameElement> pool,
        IReadOnlyList<ElementEngagement> engagements,
        GameElement excludePrimary,
        GameElement excludeSecondary)
    {
        var candidates = pool
            .Where(e => e != excludePrimary && e != excludeSecondary)
            .ToList();

        if (candidates.Count == 0)
            return excludeSecondary; // fallback: swap with secondary

        // Pick element with lowest recent engagement (freshest to student)
        var engagementMap = engagements.ToDictionary(e => e.Element, e => e.RecentInteractionRate);

        return candidates
            .OrderBy(c => engagementMap.GetValueOrDefault(c, 0))
            .First();
    }
}

// =============================================================================
// TYPES
// =============================================================================

public enum GameElement
{
    DailyStreak,
    WeeklyChallenge,
    ConceptMasteryBadge,
    LeaderboardPosition,
    StudyGroupChallenge,
    TimeTrialMode,
    MysteryReward
}

public enum RotationAction
{
    NoChange,
    IntroduceNewSecondary,
    FullRotation,
    EngagementDecayRotation
}

public record GamificationProfile(
    string StudentId,
    GameElement CurrentPrimary,
    GameElement CurrentSecondary,
    GameElement OriginalSecondary,
    DateTimeOffset PrimaryAssignedAt,
    IReadOnlyList<GameElement> AvailableElements,
    IReadOnlyList<ElementEngagement> ElementEngagements);

public record ElementEngagement(
    GameElement Element,
    int DaysSinceIntroduced,
    int DaysStable,
    double RecentInteractionRate,    // interactions per session over last 2 weeks
    double PreviousInteractionRate,  // interactions per session 2-4 weeks ago
    double EngagementDecayRate)      // (previous - recent) / previous, >0 = decay
{
    /// <summary>
    /// Compute engagement decay from two periods of interaction rates.
    /// </summary>
    public static double ComputeDecay(double previousRate, double recentRate) =>
        previousRate > 0 ? (previousRate - recentRate) / previousRate : 0;
}

public record GamificationRotation(
    GameElement Primary,
    GameElement Secondary,
    RotationAction Action,
    string Reason);
