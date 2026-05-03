// =============================================================================
// Cena Platform — Boss Attempt Document (STB-05b)
// Tracks daily boss battle attempts per student
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Tracks daily boss battle attempts for a student.
/// Resets at UTC midnight.
/// </summary>
public class BossAttemptDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string BossBattleId { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public int AttemptsUsed { get; set; } = 0;
    public int AttemptsMax { get; set; } = 3;
    public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;

    public int AttemptsRemaining => Math.Max(0, AttemptsMax - AttemptsUsed);
    public bool HasAttemptsRemaining => AttemptsUsed < AttemptsMax;

    /// <summary>
    /// Checks if this document is for today. If not, it should be reset.
    /// </summary>
    public bool IsForToday => Date == DateTime.UtcNow.Date;
}
