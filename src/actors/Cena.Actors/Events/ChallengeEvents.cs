// =============================================================================
// Cena Platform — Challenge Domain Events (STB-05b)
// Challenge starts and attempt tracking
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a student starts a challenge (daily or boss battle).
/// </summary>
public record ChallengeStarted_V1(
    string StudentId,
    string ChallengeId,
    string Kind,           // 'daily' | 'boss' | 'tournament' | 'chain'
    string? BossBattleId,  // null for non-boss challenges
    DateTimeOffset StartedAt
);

/// <summary>
/// Emitted when a boss battle attempt is consumed.
/// </summary>
public record BossAttemptConsumed_V1(
    string StudentId,
    string BossBattleId,
    int AttemptsRemaining,
    DateTimeOffset ConsumedAt
);
