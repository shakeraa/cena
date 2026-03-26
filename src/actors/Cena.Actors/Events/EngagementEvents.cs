// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Engagement Context Domain Events
// Layer: Domain Events | Runtime: .NET 9
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when XP is awarded. DifficultyMultiplier rewards mastery depth:
/// 1x recall, 2x comprehension, 3x application, 4x analysis.
/// </summary>
public record XpAwarded_V1(
    string StudentId,
    int XpAmount,
    string Source,
    int TotalXp,
    string DifficultyLevel,
    int DifficultyMultiplier
);

/// <summary>
/// Emitted when the student's daily streak is updated.
/// </summary>
public record StreakUpdated_V1(
    string StudentId,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset LastActivityDate
);

/// <summary>
/// Emitted when a student earns a badge (mastery, streak, exploration, methodology).
/// </summary>
public record BadgeEarned_V1(
    string StudentId,
    string BadgeId,
    string BadgeName,
    string BadgeCategory
);

/// <summary>
/// Emitted as a warning that a streak is about to expire.
/// Used to trigger outreach nudges.
/// </summary>
public record StreakExpiring_V1(
    string StudentId,
    int CurrentStreak,
    DateTimeOffset ExpiresAt,
    int HoursUntilExpiry
);

/// <summary>
/// Emitted when HLR predicts a concept's recall has decayed below threshold.
/// Triggers spaced repetition review scheduling.
/// </summary>
public record ReviewDue_V1(
    string StudentId,
    string ConceptId,
    double PredictedRecall,
    double HalfLifeHours,
    string Priority
);
