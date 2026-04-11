// =============================================================================
// Cena Platform -- Student Profile Snapshot (Marten inline projection)
// =============================================================================

using Cena.Actors.MethodologyHierarchy;
using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Events;

/// <summary>
/// Marten inline snapshot projection. Rebuilt every 100 events.
/// This is stored in PostgreSQL and loaded on actor activation.
/// </summary>
public class StudentProfileSnapshot
{
    // Marten requires an Id property for document storage and Query<T>
    public string Id { get => StudentId; set => StudentId = value; }

    [Pii(PiiLevel.Low, "identity")]
    public string StudentId { get; set; } = "";

    [Pii(PiiLevel.Medium, "identity")]
    public string? FullName { get; set; }

    [Pii(PiiLevel.Low, "identity")]
    public string? SchoolId { get; set; }
    public Dictionary<string, ConceptMasteryState> ConceptMastery { get; set; } = new();
    public Dictionary<string, string> ActiveMethodologyMap { get; set; } = new();
    public Dictionary<string, List<string>> MethodAttemptHistory { get; set; } = new();
    public Dictionary<string, double> HalfLifeMap { get; set; } = new();
    public int TotalXp { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTimeOffset LastActivityDate { get; set; }
    public string? ExperimentCohort { get; set; }
    public double BaselineAccuracy { get; set; }
    public double BaselineResponseTimeMs { get; set; }
    public int SessionCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // ── Account Lifecycle (LCM-001) ──
    public string AccountStatus { get; set; } = "Active";

    // ── Onboarding (STB-00) ──
    public DateTime? OnboardedAt { get; set; }
    public string? Role { get; set; }
    public string? Locale { get; set; }
    public string[] Subjects { get; set; } = Array.Empty<string>();
    public int DailyTimeGoalMinutes { get; set; }
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string Visibility { get; set; } = "class-only";

    // ── Hierarchical Methodology Maps ──
    public Dictionary<string, MethodologyAssignment> SubjectMethodologyMap { get; set; } = new();
    public Dictionary<string, MethodologyAssignment> TopicMethodologyMap { get; set; } = new();
    public Dictionary<string, MethodologyAssignment> ConceptMethodologyMap { get; set; } = new();
    public Dictionary<string, int> SessionsSinceSwitch { get; set; } = new();

    // ── Apply methods (event -> state mutation) ──

    public void Apply(ConceptAttempted_V1 e)
    {
        if (!ConceptMastery.ContainsKey(e.ConceptId))
            ConceptMastery[e.ConceptId] = new ConceptMasteryState();

        var state = ConceptMastery[e.ConceptId];
        state.PKnown = e.PosteriorMastery;
        state.TotalAttempts++;
        if (e.IsCorrect) state.CorrectCount++;
        state.LastAttemptedAt = e.Timestamp;
        state.LastMethodology = e.MethodologyActive;
    }

    /// <summary>
    /// DATA-009: Apply handler for V2 events (upcasted from V1 or natively appended).
    /// After upcasters are active, Marten delivers V2 instances to projections.
    /// </summary>
    public void Apply(ConceptAttempted_V2 e)
    {
        if (!ConceptMastery.ContainsKey(e.ConceptId))
            ConceptMastery[e.ConceptId] = new ConceptMasteryState();

        var state = ConceptMastery[e.ConceptId];
        state.PKnown = e.PosteriorMastery;
        state.TotalAttempts++;
        if (e.IsCorrect) state.CorrectCount++;
        state.LastAttemptedAt = e.Timestamp;
        state.LastMethodology = e.MethodologyActive;
    }

    public void Apply(ConceptMastered_V1 e)
    {
        if (!ConceptMastery.ContainsKey(e.ConceptId))
            ConceptMastery[e.ConceptId] = new ConceptMasteryState();

        ConceptMastery[e.ConceptId].IsMastered = true;
        ConceptMastery[e.ConceptId].MasteredAt = e.Timestamp;
        HalfLifeMap[e.ConceptId] = e.InitialHalfLifeHours;
    }

    public void Apply(MasteryDecayed_V1 e)
    {
        if (ConceptMastery.ContainsKey(e.ConceptId))
        {
            ConceptMastery[e.ConceptId].IsMastered = false;
            ConceptMastery[e.ConceptId].PKnown = e.PredictedRecall;
        }
    }

    public void Apply(MethodologySwitched_V1 e)
    {
        ActiveMethodologyMap[e.ConceptId] = e.NewMethodology;

        var clusterKey = e.ConceptId;
        if (!MethodAttemptHistory.ContainsKey(clusterKey))
            MethodAttemptHistory[clusterKey] = new();
        MethodAttemptHistory[clusterKey].Add(e.NewMethodology);
    }

    public void Apply(XpAwarded_V1 e) => TotalXp = e.TotalXp;

    public void Apply(StreakUpdated_V1 e)
    {
        CurrentStreak = e.CurrentStreak;
        LongestStreak = e.LongestStreak;
        LastActivityDate = e.LastActivityDate;
    }

    public void Apply(SessionStarted_V1 e)
    {
        SessionCount++;
        ExperimentCohort ??= e.ExperimentCohort;
        SchoolId ??= e.SchoolId; // REV-014: capture tenant once; school never changes

        // Increment cooldown counters for all tracked levels
        var keys = SessionsSinceSwitch.Keys.ToList();
        foreach (var key in keys)
            SessionsSinceSwitch[key] = SessionsSinceSwitch[key] + 1;
    }

    public void Apply(MethodologyConfidenceReached_V1 e)
    {
        // Update the assignment at the reached level to DataDriven
        var map = e.Level switch
        {
            "Subject" => SubjectMethodologyMap,
            "Topic" => TopicMethodologyMap,
            _ => ConceptMethodologyMap
        };

        if (map.TryGetValue(e.LevelId, out var existing))
        {
            map[e.LevelId] = existing with
            {
                Source = MethodologySource.DataDriven,
                ConfidenceReachedAt = e.Timestamp
            };
        }
    }

    public void Apply(MethodologySwitchDeferred_V1 e)
    {
        // Informational — no state mutation needed in snapshot
    }

    public void Apply(AccountStatusChanged_V1 e)
    {
        AccountStatus = e.NewStatus;
    }

    public void Apply(TeacherMethodologyOverride_V1 e)
    {
        if (!Enum.TryParse<Students.Methodology>(e.ToMethodology, true, out var methodology))
            return;

        var assignment = MethodologyAssignment.Default(methodology, MethodologySource.TeacherOverride)
            with { LastSwitchAt = e.Timestamp };

        switch (e.Level)
        {
            case "Subject":
                SubjectMethodologyMap[e.LevelId] = assignment;
                break;
            case "Topic":
                TopicMethodologyMap[e.LevelId] = assignment;
                break;
            default:
                ConceptMethodologyMap[e.LevelId] = assignment;
                ActiveMethodologyMap[e.LevelId] = e.ToMethodology;
                break;
        }

        SessionsSinceSwitch[e.LevelId] = 0;
    }

    /// <summary>
    /// STB-00: Apply onboarding completion event.
    /// FIND-data-007: This is the canonical profile-creation event for a student.
    /// On snapshot rebuild from the event stream, this handler is responsible for
    /// setting <see cref="CreatedAt"/> — otherwise rebuild would regress CreatedAt
    /// to DateTime.MinValue (0001-01-01). We treat the first OnboardingCompleted_V1
    /// as the canonical creation timestamp; any later replays of the same stream
    /// will re-set CreatedAt to the same value, so this is idempotent.
    /// </summary>
    public void Apply(OnboardingCompleted_V1 e)
    {
        OnboardedAt = e.CompletedAt.UtcDateTime;
        Role = e.Role;
        Locale = e.Locale;
        Subjects = e.Subjects;
        DailyTimeGoalMinutes = e.DailyTimeGoalMinutes;

        // FIND-data-007: Preserve creation timestamp across projection rebuilds.
        // Only set CreatedAt if it has not already been set (default = DateTimeOffset.MinValue).
        // This makes the first OnboardingCompleted_V1 in the stream authoritative
        // and is idempotent under replay.
        if (CreatedAt == default)
        {
            CreatedAt = e.CompletedAt;
        }
    }

    /// <summary>
    /// FIND-data-007b: Apply profile updated event.
    /// Updates mutable profile fields that students can change after onboarding.
    /// </summary>
    public void Apply(ProfileUpdated_V1 e)
    {
        if (e.DisplayName is not null)
            DisplayName = e.DisplayName;
        if (e.Bio is not null)
            Bio = e.Bio;
        if (e.Subjects is not null)
            Subjects = e.Subjects;
        if (e.Visibility is not null)
            Visibility = e.Visibility;
    }

    /// <summary>
    /// STB-01: Apply learning session started event
    /// </summary>
    public void Apply(LearningSessionStarted_V1 e)
    {
        // Track session count for analytics
        SessionCount++;
    }
}

public class ConceptMasteryState
{
    // ACT-026: Use public setters for Marten STJ deserialization roundtrip
    public double PKnown { get; set; }
    public bool IsMastered { get; set; }
    public int TotalAttempts { get; set; }
    public int CorrectCount { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? MasteredAt { get; set; }
    public string? LastMethodology { get; set; }
}
