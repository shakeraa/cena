// =============================================================================
// Cena Platform -- Challenge Documents (STB-05 HARDEN)
// Marten-backed docs that replace the hardcoded literals in
// ChallengesEndpoints.cs. Real persistence, real queries.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Daily challenge catalog entry — one row per (date, locale). A seeder
/// or content pipeline populates future dates; the endpoint looks up the
/// current day's entry. Scores are persisted in DailyChallengeCompletionDocument.
/// </summary>
public class DailyChallengeDocument
{
    public string Id { get; set; } = "";          // "daily:{yyyy-MM-dd}:{locale}"
    public DateTime Date { get; set; }            // UTC date (no time component)
    public string Locale { get; set; } = "en";
    public string ChallengeId { get; set; } = ""; // e.g. "daily_math_20260411"
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Subject { get; set; } = "Mathematics";
    public string Difficulty { get; set; } = "medium"; // 'easy' | 'medium' | 'hard'
    public DateTime ExpiresAt { get; set; }
    public string[] QuestionIds { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Per-student record of a daily challenge completion. Drives the
/// history list and the leaderboard queries.
/// </summary>
public class DailyChallengeCompletionDocument
{
    public string Id { get; set; } = "";            // "completion:{studentId}:{date}"
    public string StudentId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime Date { get; set; }              // UTC date
    public string ChallengeId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Score { get; set; }
    public int TimeSeconds { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Card chain definition — a multi-concept learning ladder. One row per chain.
/// Managed by a content pipeline; seeded on first boot with a baseline set.
/// </summary>
public class CardChainDefinitionDocument
{
    public string Id { get; set; } = "";            // chain_*
    public string ChainId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Subject { get; set; } = "";
    public int CardsTotal { get; set; }
    public string[] ConceptIds { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-student progress through a card chain. Derived/updated by the
/// content system as students master concepts.
/// </summary>
public class CardChainProgressDocument
{
    public string Id { get; set; } = "";            // "chain:{studentId}:{chainId}"
    public string StudentId { get; set; } = "";
    public string ChainId { get; set; } = "";
    public int CardsUnlocked { get; set; }
    public DateTime? LastUnlockedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tournament definition — scheduled multi-day competitive events.
/// Seeded on first boot with a baseline upcoming + active tournament.
/// </summary>
public class TournamentDocument
{
    public string Id { get; set; } = "";            // tournament_*
    public string TournamentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsActive { get; set; }
    public int ParticipantCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-student tournament registration. One row per (studentId, tournamentId).
/// </summary>
public class TournamentRegistrationDocument
{
    public string Id { get; set; } = "";            // "tourn-reg:{studentId}:{tournamentId}"
    public string StudentId { get; set; } = "";
    public string TournamentId { get; set; } = "";
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
