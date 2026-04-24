// =============================================================================
// Cena Platform -- ConceptMasteryState (Domain Value Object)
// MST-001: Per-concept knowledge record stored in StudentActor state
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Actors.Mastery;

/// <summary>
/// Immutable per-concept mastery state. Every mastery computation reads/writes this record.
/// Stored as part of the event-sourced StudentActor snapshot.
/// </summary>
public sealed record ConceptMasteryState
{
    // === Core mastery signals ===
    public float MasteryProbability { get; init; }
    public float HalfLifeHours { get; init; }
    public DateTimeOffset LastInteraction { get; init; }
    public DateTimeOffset FirstEncounter { get; init; }

    // === Performance counters ===
    public int AttemptCount { get; init; }
    public int CorrectCount { get; init; }
    public int CurrentStreak { get; init; }

    // === Qualitative signals ===
    public int BloomLevel { get; init; }
    public float SelfConfidence { get; init; }
    public ErrorType[] RecentErrors { get; init; } = Array.Empty<ErrorType>();
    public MasteryQuality QualityQuadrant { get; init; }

    // === Spaced repetition (FSRS-compatible) ===
    public float Stability { get; init; }
    public float Difficulty { get; init; }

    // === Method tracking ===
    public MethodAttempt[] MethodHistory { get; init; } = Array.Empty<MethodAttempt>();

    // === Computed properties ===

    /// <summary>
    /// HLR recall probability: 2^(-deltaHours / HalfLifeHours).
    /// Returns 0.0 if never interacted or half-life is invalid.
    /// </summary>
    public float RecallProbability(DateTimeOffset now)
    {
        if (LastInteraction == default || HalfLifeHours <= 0f)
            return 0.0f;

        var deltaHours = (now - LastInteraction).TotalHours;
        if (deltaHours <= 0)
            return 1.0f;

        return (float)Math.Pow(2.0, -deltaHours / HalfLifeHours);
    }

    /// <summary>
    /// Rolling accuracy: CorrectCount / AttemptCount. Returns 0.0 if no attempts.
    /// </summary>
    public float RollingAccuracy =>
        AttemptCount == 0 ? 0.0f : CorrectCount / (float)AttemptCount;

    /// <summary>
    /// Current mastery level based on MasteryProbability thresholds.
    /// </summary>
    [JsonIgnore]
    public MasteryLevel MasteryLevel => MasteryProbability switch
    {
        >= MasteryThreshold.Proficient => MasteryLevel.Mastered,
        >= MasteryThreshold.Developing => MasteryLevel.Proficient,
        >= MasteryThreshold.Introduced => MasteryLevel.Developing,
        >= MasteryThreshold.NotStarted => MasteryLevel.Introduced,
        _ => MasteryLevel.NotStarted
    };

    /// <summary>
    /// True when concept was mastered but recall has dropped below decay warning.
    /// </summary>
    public bool IsDecaying(DateTimeOffset now) =>
        MasteryProbability >= MasteryThreshold.Proficient &&
        RecallProbability(now) < MasteryThreshold.DecayWarning;

    // === With-methods for immutable updates ===

    public ConceptMasteryState WithBktUpdate(float newProbability) =>
        this with { MasteryProbability = newProbability };

    public ConceptMasteryState WithAttempt(bool correct, DateTimeOffset now)
    {
        var newAttemptCount = AttemptCount + 1;
        var newCorrectCount = correct ? CorrectCount + 1 : CorrectCount;
        var newStreak = correct ? CurrentStreak + 1 : 0;
        var newFirstEncounter = FirstEncounter == default ? now : FirstEncounter;

        return this with
        {
            AttemptCount = newAttemptCount,
            CorrectCount = newCorrectCount,
            CurrentStreak = newStreak,
            LastInteraction = now,
            FirstEncounter = newFirstEncounter
        };
    }

    public ConceptMasteryState WithHalfLifeUpdate(float newHalfLife) =>
        this with { HalfLifeHours = newHalfLife };

    public ConceptMasteryState WithBloomLevel(int level)
    {
        if (level < 0 || level > 6)
            throw new ArgumentOutOfRangeException(nameof(level), "Bloom level must be 0-6");
        return this with { BloomLevel = level };
    }

    public ConceptMasteryState WithMethodAttempt(MethodAttempt attempt) =>
        this with
        {
            MethodHistory = MethodHistory.Length == 0
                ? new[] { attempt }
                : MethodHistory.Append(attempt).ToArray()
        };

    public ConceptMasteryState WithRecentError(ErrorType error)
    {
        var errors = RecentErrors.Length >= 10
            ? RecentErrors.Skip(1).Append(error).ToArray()
            : RecentErrors.Append(error).ToArray();
        return this with { RecentErrors = errors };
    }
}
