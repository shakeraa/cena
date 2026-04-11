// =============================================================================
// Cena Platform — Analytics Rollup Service (STB-09b)
// Computes and maintains analytics projections
// =============================================================================

using Cena.Actors.Serving;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Projections;

public class AnalyticsRollupService : IAnalyticsRollupService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<AnalyticsRollupService> _logger;

    public AnalyticsRollupService(
        IDocumentStore store,
        ILogger<AnalyticsRollupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task RecordStudyTimeAsync(
        string studentId, 
        DateTime date, 
        string subject, 
        string activityType, 
        int minutes,
        CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        
        var id = $"{studentId}:{date:yyyy-MM-dd}";
        var breakdown = await session.LoadAsync<StudentTimeBreakdown>(id, ct) 
            ?? new StudentTimeBreakdown
            {
                Id = id,
                StudentId = studentId,
                Date = date.Date,
                BySubject = new Dictionary<string, int>(),
                ByActivity = new ActivityTimeBreakdown(),
                HourlyDistribution = new Dictionary<int, int>()
            };

        // Update totals
        breakdown.TotalMinutes += minutes;
        
        // Update subject breakdown
        if (!breakdown.BySubject.ContainsKey(subject))
            breakdown.BySubject[subject] = 0;
        breakdown.BySubject[subject] += minutes;

        // Update activity breakdown
        switch (activityType.ToLowerInvariant())
        {
            case "questions":
            case "practice":
                breakdown.ByActivity.QuestionsMinutes += minutes;
                break;
            case "review":
            case "srs":
                breakdown.ByActivity.ReviewMinutes += minutes;
                break;
            case "tutoring":
                breakdown.ByActivity.TutoringMinutes += minutes;
                break;
            case "challenge":
            case "boss":
                breakdown.ByActivity.ChallengeMinutes += minutes;
                break;
        }

        // Update hourly distribution
        var hour = DateTime.UtcNow.Hour;
        if (!breakdown.HourlyDistribution.ContainsKey(hour))
            breakdown.HourlyDistribution[hour] = 0;
        breakdown.HourlyDistribution[hour] += minutes;

        breakdown.UpdatedAt = DateTime.UtcNow;
        session.Store(breakdown);
        await session.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Recorded {Minutes}min for student {StudentId} on {Date} - Subject: {Subject}, Activity: {Activity}",
            minutes, studentId, date.ToString("yyyy-MM-dd"), subject, activityType);
    }

    public async Task RecordAnswerAsync(
        string studentId, 
        bool isCorrect, 
        int timeSpentSeconds, 
        string? focusState,
        DateTime timestamp,
        CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var id = studentId;
        var profile = await session.LoadAsync<StudentFlowAccuracyProfile>(id, ct)
            ?? new StudentFlowAccuracyProfile
            {
                Id = id,
                StudentId = studentId,
                ByFocusState = new Dictionary<string, FlowAccuracyStats>(),
                BySessionLength = new Dictionary<string, FlowAccuracyStats>(),
                ByTimeOfDay = new Dictionary<string, FlowAccuracyStats>(),
                Overall = new FlowAccuracyStats()
            };

        // Determine session length bucket
        var sessionLengthBucket = timeSpentSeconds switch
        {
            < 30 => "quick",
            < 120 => "normal",
            _ => "long"
        };

        // Determine time of day
        var hour = timestamp.Hour;
        var timeOfDay = hour switch
        {
            >= 6 and < 12 => "morning",
            >= 12 and < 18 => "afternoon",
            >= 18 and < 22 => "evening",
            _ => "night"
        };

        // Use provided focus state or default
        var state = string.IsNullOrEmpty(focusState) ? "unknown" : focusState.ToLowerInvariant();

        // Update stats for each dimension
        UpdateStats(profile.ByFocusState, state, isCorrect, timeSpentSeconds);
        UpdateStats(profile.BySessionLength, sessionLengthBucket, isCorrect, timeSpentSeconds);
        UpdateStats(profile.ByTimeOfDay, timeOfDay, isCorrect, timeSpentSeconds);
        UpdateStats(profile.Overall, isCorrect, timeSpentSeconds);

        // Update best time recommendation
        profile.Overall.BestTimeRecommendation = FindBestTime(profile.ByTimeOfDay);

        profile.UpdatedAt = DateTime.UtcNow;
        session.Store(profile);
        await session.SaveChangesAsync(ct);
    }

    public async Task<StudentTimeBreakdown?> GetTimeBreakdownAsync(
        string studentId, 
        DateTime date,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var id = $"{studentId}:{date:yyyy-MM-dd}";
        return await session.LoadAsync<StudentTimeBreakdown>(id, ct);
    }

    public async Task<StudentFlowAccuracyProfile?> GetFlowAccuracyProfileAsync(
        string studentId,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<StudentFlowAccuracyProfile>(studentId, ct);
    }

    public async Task<IReadOnlyList<StudentTimeBreakdown>> GetTimeRangeAsync(
        string studentId, 
        DateTime from, 
        DateTime to,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        
        var results = new List<StudentTimeBreakdown>();
        var current = from.Date;
        
        while (current <= to.Date)
        {
            var id = $"{studentId}:{current:yyyy-MM-dd}";
            var breakdown = await session.LoadAsync<StudentTimeBreakdown>(id, ct);
            if (breakdown != null)
                results.Add(breakdown);
            current = current.AddDays(1);
        }

        return results;
    }

    private static void UpdateStats(
        Dictionary<string, FlowAccuracyStats> dict, 
        string key, 
        bool isCorrect, 
        int timeSpentSeconds)
    {
        if (!dict.ContainsKey(key))
            dict[key] = new FlowAccuracyStats();

        var stats = dict[key];
        var accuracy = isCorrect ? 1.0 : 0.0;
        
        // Rolling average update
        stats.SampleCount++;
        stats.AvgAccuracy = ((stats.AvgAccuracy * (stats.SampleCount - 1)) + accuracy) / stats.SampleCount;
        stats.AvgResponseTimeSeconds = ((stats.AvgResponseTimeSeconds * (stats.SampleCount - 1)) + timeSpentSeconds) / stats.SampleCount;
        
        // Estimate flow score based on accuracy and speed
        var speedScore = Math.Max(0, 1.0 - (timeSpentSeconds / 60.0)); // Faster = higher
        stats.AvgFlowScore = (stats.AvgAccuracy * 0.7) + (speedScore * 0.3);
    }

    private static void UpdateStats(FlowAccuracyStats stats, bool isCorrect, int timeSpentSeconds)
    {
        var accuracy = isCorrect ? 1.0 : 0.0;
        stats.SampleCount++;
        stats.AvgAccuracy = ((stats.AvgAccuracy * (stats.SampleCount - 1)) + accuracy) / stats.SampleCount;
        stats.AvgResponseTimeSeconds = ((stats.AvgResponseTimeSeconds * (stats.SampleCount - 1)) + timeSpentSeconds) / stats.SampleCount;
        
        var speedScore = Math.Max(0, 1.0 - (timeSpentSeconds / 60.0));
        stats.AvgFlowScore = (stats.AvgAccuracy * 0.7) + (speedScore * 0.3);
    }

    private static string? FindBestTime(Dictionary<string, FlowAccuracyStats> byTimeOfDay)
    {
        if (byTimeOfDay.Count == 0) return null;
        
        var best = byTimeOfDay
            .Where(kvp => kvp.Value.SampleCount >= 5) // Minimum sample size
            .OrderByDescending(kvp => kvp.Value.AvgFlowScore)
            .FirstOrDefault();

        return best.Key;
    }
}

/// <summary>
/// Event emitted when study time is recorded (for cross-service coordination)
/// </summary>
public record StudyTimeRecorded_V1(
    string StudentId,
    DateTime Date,
    string Subject,
    string ActivityType,
    int Minutes,
    DateTimeOffset RecordedAt
);

/// <summary>
/// Event emitted when answer analytics are recorded
/// </summary>
public record AnswerAnalyticsRecorded_V1(
    string StudentId,
    bool IsCorrect,
    int TimeSpentSeconds,
    string? FocusState,
    DateTimeOffset Timestamp
);
