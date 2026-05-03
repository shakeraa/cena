// =============================================================================
// Cena Platform -- Challenges API DTOs (STB-05 Phase 1)
// Daily challenges, boss battles, card chains, and tournaments
// =============================================================================

namespace Cena.Api.Contracts.Challenges;

// ═════════════════════════════════════════════════════════════════════════════
// Daily Challenge DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record DailyChallengeDto(
    string ChallengeId,
    string Title,
    string Description,
    string Subject,
    string Difficulty,        // 'easy' | 'medium' | 'hard'
    DateTime ExpiresAt,
    bool Attempted,
    int? BestScore);

public record DailyChallengeLeaderboardDto(
    DailyChallengeLeaderboardEntry[] Entries,
    int CurrentStudentRank);

public record DailyChallengeLeaderboardEntry(
    int Rank,
    string StudentId,
    string DisplayName,
    int Score,
    int TimeSeconds);

public record DailyChallengeHistoryDto(DailyChallengeHistoryEntry[] Entries);

public record DailyChallengeHistoryEntry(
    DateTime Date,
    string Title,
    bool Attempted,
    int? Score);

// ═════════════════════════════════════════════════════════════════════════════
// Boss Battle DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record BossBattleListDto(
    BossBattleSummary[] Available,
    BossBattleSummary[] Locked);

public record BossBattleSummary(
    string BossBattleId,
    string Name,
    string Subject,
    string Difficulty,
    int RequiredMasteryLevel);

public record BossBattleDetailDto(
    string BossBattleId,
    string Name,
    string Description,
    string Subject,
    string Difficulty,
    int AttemptsRemaining,
    int AttemptsMax,
    BossBattleReward[] Rewards);

public record BossBattleReward(string Type, int Amount);

// ═════════════════════════════════════════════════════════════════════════════
// Card Chain DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record CardChainListDto(CardChainSummary[] Chains);

public record CardChainSummary(
    string ChainId,
    string Name,
    int CardsUnlocked,
    int CardsTotal,
    DateTime? LastUnlockedAt);

// ═════════════════════════════════════════════════════════════════════════════
// Tournament DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record TournamentListDto(
    TournamentSummary[] Upcoming,
    TournamentSummary[] Active);

public record TournamentSummary(
    string TournamentId,
    string Name,
    DateTime StartsAt,
    DateTime EndsAt,
    int ParticipantCount,
    bool IsRegistered);
