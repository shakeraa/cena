// =============================================================================
// Cena Platform -- Gamification DTOs (STB-03 Phase 1)
// Badges, XP, streak, and leaderboard contracts
// =============================================================================

namespace Cena.Api.Contracts.Gamification;

public record BadgeListResponse(
    Badge[] Earned,
    Badge[] Locked);

public record Badge(
    string BadgeId,
    string Name,
    string Description,
    string IconName,
    string Tier,        // 'bronze' | 'silver' | 'gold' | 'platinum'
    DateTime? EarnedAt);

public record XpStatusDto(
    int CurrentLevel,
    int CurrentXp,
    int XpToNextLevel,
    int TotalXpEarned);

public record StreakStatusDto(
    int CurrentDays,
    int LongestDays,
    DateTime? LastActivityAt,
    bool IsAtRisk);

public record LeaderboardDto(
    string Scope,
    LeaderboardEntry[] Entries,
    int CurrentStudentRank);

public record LeaderboardEntry(
    int Rank,
    string StudentId,
    string DisplayName,
    int Xp,
    string? AvatarUrl);
