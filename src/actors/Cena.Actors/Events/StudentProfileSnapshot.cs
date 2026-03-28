// =============================================================================
// Cena Platform -- Student Profile Snapshot (Marten inline projection)
// =============================================================================

using Cena.Actors.MethodologyHierarchy;

namespace Cena.Actors.Events;

/// <summary>
/// Marten inline snapshot projection. Rebuilt every 100 events.
/// This is stored in PostgreSQL and loaded on actor activation.
/// </summary>
public class StudentProfileSnapshot
{
    public string StudentId { get; set; } = "";
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
