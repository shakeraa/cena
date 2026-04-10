// =============================================================================
// Cena Platform -- Active Session Projection (STB-01)
// Tracks a student's currently active learning session
// =============================================================================

namespace Cena.Actors.Projections;

/// <summary>
/// Marten inline projection for active session state.
/// One document per student. Deleted when session ends.
/// </summary>
public class ActiveSessionSnapshot
{
    // Marten requires Id property
    public string Id { get; set; } = "";
    
    public string StudentId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string[] Subjects { get; set; } = Array.Empty<string>();
    public string Mode { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public int DurationMinutes { get; set; }
    public int QuestionsAttempted { get; set; }
    public string? CurrentQuestionId { get; set; }

    /// <summary>
    /// Calculate progress percentage based on time elapsed vs duration
    /// </summary>
    public int GetProgressPercent()
    {
        if (DurationMinutes <= 0) return 0;
        var elapsed = DateTime.UtcNow - StartedAt;
        var percent = (int)(elapsed.TotalMinutes / DurationMinutes * 100);
        return Math.Min(100, Math.Max(0, percent));
    }
}
