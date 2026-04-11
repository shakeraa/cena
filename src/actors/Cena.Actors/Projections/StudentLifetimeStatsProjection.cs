// =============================================================================
// Cena Platform — Student Lifetime Statistics Projection (FIND-data-009)
// Maintains per-student aggregate stats for fast analytics queries.
// Replaces full event store scans (QueryAllRawEvents) with single-document lookup.
// =============================================================================

using Cena.Actors.Events;
using Marten.Events.Aggregation;

namespace Cena.Actors.Projections;

/// <summary>
/// Read model for student lifetime statistics. One document per student.
/// Updated by inline projection as events are appended.
/// </summary>
public class StudentLifetimeStats
{
    public string Id { get; set; } = ""; // Same as StudentId
    public string StudentId { get; set; } = "";
    
    // Session stats
    public int TotalSessions { get; set; }
    public DateTimeOffset? LastSessionAt { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    
    // Attempt stats
    public int TotalAttempts { get; set; }
    public int TotalCorrect { get; set; }
    public double Accuracy => TotalAttempts > 0 ? (double)TotalCorrect / TotalAttempts : 0;
    
    // Challenge/Boss stats
    public int ChallengesCompleted { get; set; }
    public int BossesDefeated { get; set; }
    
    // Badge stats
    public int BadgeCount { get; set; }
    public List<string> BadgeIds { get; set; } = new();
    
    // Timestamps
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Inline projection that builds StudentLifetimeStats from student events.
/// FIND-data-009: Replaces QueryAllRawEvents full-scans with single-document lookup.
/// </summary>
public class StudentLifetimeStatsProjection : SingleStreamProjection<StudentLifetimeStats, string>
{
    public StudentLifetimeStats Create(LearningSessionStarted_V1 e)
    {
        return new StudentLifetimeStats
        {
            Id = e.StudentId,
            StudentId = e.StudentId,
            TotalSessions = 1,
            LastSessionAt = e.StartedAt,
            CurrentStreak = 1,
            LongestStreak = 1,
            CreatedAt = e.StartedAt,
            UpdatedAt = e.StartedAt
        };
    }

    public void Apply(ConceptAttempted_V1 e, StudentLifetimeStats stats)
    {
        stats.TotalAttempts++;
        if (e.IsCorrect)
            stats.TotalCorrect++;
        stats.UpdatedAt = e.Timestamp;
    }

    public void Apply(ConceptAttempted_V2 e, StudentLifetimeStats stats)
    {
        stats.TotalAttempts++;
        if (e.IsCorrect)
            stats.TotalCorrect++;
        stats.UpdatedAt = e.Timestamp;
    }

    public void Apply(LearningSessionStarted_V1 e, StudentLifetimeStats stats)
    {
        stats.TotalSessions++;
        stats.LastSessionAt = e.StartedAt;
        
        // Streak calculation (simplified: consecutive days)
        if (stats.LastSessionAt.HasValue)
        {
            var daysSinceLast = (e.StartedAt - stats.LastSessionAt.Value).TotalDays;
            if (daysSinceLast <= 1)
            {
                stats.CurrentStreak++;
                stats.LongestStreak = Math.Max(stats.LongestStreak, stats.CurrentStreak);
            }
            else
            {
                stats.CurrentStreak = 1;
            }
        }
        
        stats.UpdatedAt = e.StartedAt;
    }

    public void Apply(ChallengeCompleted_V1 e, StudentLifetimeStats stats)
    {
        stats.ChallengesCompleted++;
        if (e.IsBoss)
            stats.BossesDefeated++;
        stats.UpdatedAt = e.CompletedAt;
    }

    public void Apply(BadgeEarned_V1 e, StudentLifetimeStats stats)
    {
        stats.BadgeCount++;
        if (!stats.BadgeIds.Contains(e.BadgeId))
            stats.BadgeIds.Add(e.BadgeId);
        stats.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
