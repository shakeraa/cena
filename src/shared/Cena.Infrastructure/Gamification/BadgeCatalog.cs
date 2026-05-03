// =============================================================================
// Cena Platform — Badge Catalog (STB-03c)
// Full badge definitions with unlock criteria and rewards
// =============================================================================

namespace Cena.Infrastructure.Gamification;

/// <summary>
/// Static catalog of all available badges.
/// </summary>
public static class BadgeCatalog
{
    public static IReadOnlyList<BadgeDefinition> All => new[]
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Milestone Badges (Session/Question Counts)
        // ═══════════════════════════════════════════════════════════════════════
        new BadgeDefinition(
            "first-steps",
            "First Steps",
            "Complete your first learning session",
            "mdi-shoe-print",
            "bronze",
            new BadgeCriteria(BadgeCriteriaType.SessionCount, 1)),

        new BadgeDefinition(
            "session-starter",
            "Session Starter",
            "Complete 10 learning sessions",
            "mdi-play-circle",
            "bronze",
            new BadgeCriteria(BadgeCriteriaType.SessionCount, 10)),

        new BadgeDefinition(
            "dedicated-learner",
            "Dedicated Learner",
            "Complete 50 learning sessions",
            "mdi-school",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.SessionCount, 50)),

        new BadgeDefinition(
            "learning-master",
            "Learning Master",
            "Complete 100 learning sessions",
            "mdi-trophy",
            "gold",
            new BadgeCriteria(BadgeCriteriaType.SessionCount, 100)),

        new BadgeDefinition(
            "question-pioneer",
            "Question Pioneer",
            "Answer your first question correctly",
            "mdi-help-circle",
            "bronze",
            new BadgeCriteria(BadgeCriteriaType.CorrectAnswers, 1)),

        new BadgeDefinition(
            "quiz-apprentice",
            "Quiz Apprentice",
            "Answer 50 questions correctly",
            "mdi-check-circle",
            "bronze",
            new BadgeCriteria(BadgeCriteriaType.CorrectAnswers, 50)),

        new BadgeDefinition(
            "quiz-master",
            "Quiz Master",
            "Answer 200 questions correctly",
            "mdi-medal",
            "gold",
            new BadgeCriteria(BadgeCriteriaType.CorrectAnswers, 200)),

        // ═══════════════════════════════════════════════════════════════════════
        // Streak Badges
        // ═══════════════════════════════════════════════════════════════════════
        new BadgeDefinition(
            "week-streak",
            "Week Streak",
            "Maintain a 7-day learning streak",
            "mdi-calendar-week",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.StreakDays, 7)),

        new BadgeDefinition(
            "month-streak",
            "Month Streak",
            "Maintain a 30-day learning streak",
            "mdi-calendar-month",
            "gold",
            new BadgeCriteria(BadgeCriteriaType.StreakDays, 30)),

        // ═══════════════════════════════════════════════════════════════════════
        // Accuracy Badges
        // ═══════════════════════════════════════════════════════════════════════
        new BadgeDefinition(
            "sharp-shooter",
            "Sharp Shooter",
            "Achieve 90% accuracy in a single session",
            "mdi-bullseye",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.SessionAccuracy, 90)),

        new BadgeDefinition(
            "perfect-session",
            "Perfect Session",
            "Answer 10+ questions with 100% accuracy",
            "mdi-star-circle",
            "gold",
            new BadgeCriteria(BadgeCriteriaType.PerfectSession, 10)),

        // ═══════════════════════════════════════════════════════════════════════
        // Subject Badges
        // ═══════════════════════════════════════════════════════════════════════
        new BadgeDefinition(
            "math-whiz",
            "Math Whiz",
            "Master 5 math concepts",
            "mdi-calculator",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.MasteredConcepts, 5, "Mathematics")),

        new BadgeDefinition(
            "science-explorer",
            "Science Explorer",
            "Master 5 science concepts",
            "mdi-flask",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.MasteredConcepts, 5, "Science")),

        // ═══════════════════════════════════════════════════════════════════════
        // Social Badges
        // ═══════════════════════════════════════════════════════════════════════
        new BadgeDefinition(
            "social-butterfly",
            "Social Butterfly",
            "Add 5 friends",
            "mdi-account-group",
            "bronze",
            new BadgeCriteria(BadgeCriteriaType.FriendsCount, 5)),

        new BadgeDefinition(
            "helpful-peer",
            "Helpful Peer",
            "Send 10 friend requests",
            "mdi-handshake",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.FriendRequestsSent, 10)),

        // ═══════════════════════════════════════════════════════════════════════
        // Challenge Badges
        // ═══════════════════════════════════════════════════════════════════════
        new BadgeDefinition(
            "boss-slayer",
            "Boss Slayer",
            "Defeat your first boss battle",
            "mdi-sword",
            "silver",
            new BadgeCriteria(BadgeCriteriaType.BossesDefeated, 1)),

        new BadgeDefinition(
            "daily-warrior",
            "Daily Warrior",
            "Complete 5 daily challenges",
            "mdi-calendar-check",
            "bronze",
            new BadgeCriteria(BadgeCriteriaType.DailyChallengesCompleted, 5)),
    };

    public static BadgeDefinition? GetById(string id) => 
        All.FirstOrDefault(b => b.Id == id);

    public static IReadOnlyList<BadgeDefinition> GetByTier(string tier) =>
        All.Where(b => b.Tier == tier).ToList();
}

/// <summary>
/// Badge definition with unlock criteria.
/// </summary>
public record BadgeDefinition(
    string Id,
    string Name,
    string Description,
    string IconName,
    string Tier, // bronze | silver | gold | platinum
    BadgeCriteria Criteria);

/// <summary>
/// Criteria for unlocking a badge.
/// </summary>
public record BadgeCriteria(
    BadgeCriteriaType Type,
    int Threshold,
    string? SubjectFilter = null);

public enum BadgeCriteriaType
{
    SessionCount,
    CorrectAnswers,
    StreakDays,
    SessionAccuracy,
    PerfectSession,
    MasteredConcepts,
    FriendsCount,
    FriendRequestsSent,
    BossesDefeated,
    DailyChallengesCompleted
}
