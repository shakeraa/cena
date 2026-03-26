// =============================================================================
// Cena Platform -- Student Profile Snapshot (Marten inline projection)
// =============================================================================

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

    // ── Apply methods (event -> state mutation) ──

    public void Apply(ConceptAttempted_V1 e)
    {
        if (!ConceptMastery.ContainsKey(e.ConceptId))
            ConceptMastery[e.ConceptId] = new ConceptMasteryState();

        var state = ConceptMastery[e.ConceptId];
        state.PKnown = e.PosteriorMastery;
        state.TotalAttempts++;
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
    }
}

public class ConceptMasteryState
{
    public double PKnown { get; set; }
    public bool IsMastered { get; set; }
    public int TotalAttempts { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? MasteredAt { get; set; }
    public string? LastMethodology { get; set; }
}
