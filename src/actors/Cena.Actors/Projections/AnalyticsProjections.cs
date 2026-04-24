// =============================================================================
// Cena Platform — Analytics Projections (STB-09b)
// Time breakdown and flow-vs-accuracy projections for student analytics
// =============================================================================

namespace Cena.Actors.Projections;

/// <summary>
/// Daily time breakdown rollup for a student.
/// Aggregates study time by day and subject.
/// </summary>
public class StudentTimeBreakdown
{
    public string Id { get; set; } = ""; // studentId:yyyy-MM-dd
    public string StudentId { get; set; } = "";
    public DateTime Date { get; set; }
    
    /// <summary>
    /// Total study time in minutes for the day
    /// </summary>
    public int TotalMinutes { get; set; }
    
    /// <summary>
    /// Time breakdown by subject (subject -> minutes)
    /// </summary>
    public Dictionary<string, int> BySubject { get; set; } = new();
    
    /// <summary>
    /// Time breakdown by activity type
    /// </summary>
    public ActivityTimeBreakdown ByActivity { get; set; } = new();
    
    /// <summary>
    /// Hourly distribution (0-23 -> minutes)
    /// </summary>
    public Dictionary<int, int> HourlyDistribution { get; set; } = new();
    
    public DateTime UpdatedAt { get; set; }
}

public class ActivityTimeBreakdown
{
    public int QuestionsMinutes { get; set; }
    public int ReviewMinutes { get; set; }
    public int TutoringMinutes { get; set; }
    public int ChallengeMinutes { get; set; }
}

/// <summary>
/// Weekly time summary for longer-term analytics
/// </summary>
public class StudentWeeklyTimeSummary
{
    public string Id { get; set; } = ""; // studentId:yyyy-Www
    public string StudentId { get; set; } = "";
    public int Year { get; set; }
    public int WeekNumber { get; set; }
    
    public int TotalMinutes { get; set; }
    public Dictionary<string, int> BySubject { get; set; } = new();
    public int SessionsCount { get; set; }
    public int QuestionsAnswered { get; set; }
    
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Flow state vs accuracy correlation data for a student.
/// Tracks performance across different cognitive states.
/// </summary>
public class StudentFlowAccuracyProfile
{
    public string Id { get; set; } = ""; // studentId
    public string StudentId { get; set; } = "";
    
    /// <summary>
    /// Stats by focus state (Strong, Stable, Declining, etc.)
    /// </summary>
    public Dictionary<string, FlowAccuracyStats> ByFocusState { get; set; } = new();
    
    /// <summary>
    /// Stats by session duration bucket
    /// </summary>
    public Dictionary<string, FlowAccuracyStats> BySessionLength { get; set; } = new();
    
    /// <summary>
    /// Stats by time of day
    /// </summary>
    public Dictionary<string, FlowAccuracyStats> ByTimeOfDay { get; set; } = new();
    
    /// <summary>
    /// Overall rolling averages
    /// </summary>
    public FlowAccuracyStats Overall { get; set; } = new();
    
    public DateTime UpdatedAt { get; set; }
}

public class FlowAccuracyStats
{
    /// <summary>
    /// Average accuracy (0.0-1.0)
    /// </summary>
    public double AvgAccuracy { get; set; }
    
    /// <summary>
    /// Average response time in seconds
    /// </summary>
    public double AvgResponseTimeSeconds { get; set; }
    
    /// <summary>
    /// Number of data points
    /// </summary>
    public int SampleCount { get; set; }
    
    /// <summary>
    /// Average flow/focus score (0.0-1.0)
    /// </summary>
    public double AvgFlowScore { get; set; }
    
    /// <summary>
    /// Best performing time (for recommendations)
    /// </summary>
    public string? BestTimeRecommendation { get; set; }
}

/// <summary>
/// Subject mastery progression over time
/// </summary>
public class SubjectMasteryTimeline
{
    public string Id { get; set; } = ""; // studentId:subject
    public string StudentId { get; set; } = "";
    public string Subject { get; set; } = "";
    
    /// <summary>
    /// Daily mastery snapshots
    /// </summary>
    public List<MasterySnapshot> Snapshots { get; set; } = new();
    
    public DateTime UpdatedAt { get; set; }
}

public class MasterySnapshot
{
    public DateTime Date { get; set; }
    public double AverageMastery { get; set; }
    public int ConceptsAttempted { get; set; }
    public int ConceptsMastered { get; set; }
    public double Accuracy { get; set; }
}

/// <summary>
/// Analytics rollup service for computing and updating analytics projections
/// </summary>
public interface IAnalyticsRollupService
{
    Task RecordStudyTimeAsync(string studentId, DateTime date, string subject, 
        string activityType, int minutes, CancellationToken ct = default);
    
    Task RecordAnswerAsync(string studentId, bool isCorrect, int timeSpentSeconds, 
        string? focusState, DateTime timestamp, CancellationToken ct = default);
    
    Task<StudentTimeBreakdown?> GetTimeBreakdownAsync(string studentId, 
        DateTime date, CancellationToken ct = default);
    
    Task<StudentFlowAccuracyProfile?> GetFlowAccuracyProfileAsync(string studentId, 
        CancellationToken ct = default);
    
    Task<IReadOnlyList<StudentTimeBreakdown>> GetTimeRangeAsync(string studentId, 
        DateTime from, DateTime to, CancellationToken ct = default);
}
