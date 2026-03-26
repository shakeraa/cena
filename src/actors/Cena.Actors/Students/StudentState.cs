// =============================================================================
// Cena Platform -- StudentState (Event-Sourced Aggregate State)
// Layer: Actor Model | Runtime: .NET 9
//
// In-memory state for a single student actor. Rebuilt from Marten snapshot +
// event replay on activation. Zero database round-trips for reads.
// Memory budget: ~500KB per actor instance.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Students;

/// <summary>
/// In-memory state for a single student actor. Rebuilt from Marten snapshot +
/// event replay on activation. All read queries against student state are served
/// from this in-memory snapshot.
/// </summary>
public sealed class StudentState
{
    // ---- Identity ----
    public string StudentId { get; set; } = "";

    // ---- Mastery Overlay (concept -> P(known) via BKT) ----
    /// <summary>
    /// Maps concept ID to current P(known). Updated on every AttemptConcept.
    /// Max tracked concepts per student: 2000 (soft limit).
    /// </summary>
    public Dictionary<string, double> MasteryMap { get; set; } = new();

    // ---- Methodology State ----
    /// <summary>Concept ID -> currently active methodology name.</summary>
    public Dictionary<string, Methodology> MethodologyMap { get; set; } = new();

    /// <summary>Concept cluster -> ordered list of methodologies tried.</summary>
    public Dictionary<string, List<MethodologyAttemptRecord>> MethodAttemptHistory { get; set; } = new();

    // ---- Attempt History (sliding window for baselines) ----
    /// <summary>
    /// Last 20 attempts across all concepts -- used for baseline accuracy and
    /// response time. Circular buffer semantics: oldest evicted on overflow.
    /// </summary>
    public List<AttemptRecord> RecentAttempts { get; set; } = new(MaxRecentAttempts);

    // ---- Half-Life Regression Timers ----
    /// <summary>Concept ID -> HLR state (half-life in hours, last review time).</summary>
    public Dictionary<string, HlrState> HlrTimers { get; set; } = new();

    // ---- Engagement ----
    public int TotalXp { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTimeOffset LastActivityDate { get; set; }

    // ---- Cognitive Load Profile ----
    /// <summary>Trailing fatigue baseline -- median fatigue score over last 5 sessions.</summary>
    public double BaselineFatigueScore { get; set; }
    public double BaselineAccuracy { get; set; }
    public double BaselineResponseTimeMs { get; set; }

    // ---- Metadata ----
    public string? ExperimentCohort { get; set; }
    public int SessionCount { get; set; }
    public int EventVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSnapshotAt { get; set; }

    // ---- Active Session ----
    /// <summary>Non-null when a learning session is in progress.</summary>
    public string? ActiveSessionId { get; set; }

    // ---- Constants ----
    public const int MaxRecentAttempts = 20;
    public const int MaxTrackedConcepts = 2000;
    public const int SnapshotInterval = 100;
    public const long MemoryBudgetBytes = 512 * 1024; // 500KB

    // =========================================================================
    // APPLY METHODS -- event-sourced projection pattern
    // Each Apply overload handles one event type. Called during snapshot rebuild
    // and live aggregation. KEEP THESE ALLOCATION-FREE on the hot path.
    // =========================================================================

    /// <summary>
    /// Apply a concept attempt -- the primary hot-path event.
    /// Updates mastery map, recent attempts buffer, and baseline metrics.
    /// Uses event timestamp for deterministic replay -- never wall clock.
    /// </summary>
    public void Apply(ConceptAttempted_V1 e)
    {
        MasteryMap[e.ConceptId] = e.PosteriorMastery;

        // Circular buffer: evict oldest when full
        if (RecentAttempts.Count >= MaxRecentAttempts)
            RecentAttempts.RemoveAt(0);

        RecentAttempts.Add(new AttemptRecord(
            e.ConceptId, e.IsCorrect, e.ResponseTimeMs,
            e.ErrorType, e.MethodologyActive, e.Timestamp));

        LastActivityDate = e.Timestamp;
        RecalculateBaselines();
        EventVersion++;
    }

    public void Apply(ConceptMastered_V1 e)
    {
        MasteryMap[e.ConceptId] = e.MasteryLevel;
        HlrTimers[e.ConceptId] = new HlrState(e.InitialHalfLifeHours, e.Timestamp);
        EventVersion++;
    }

    public void Apply(MasteryDecayed_V1 e)
    {
        if (MasteryMap.ContainsKey(e.ConceptId))
            MasteryMap[e.ConceptId] = e.PredictedRecall;

        if (HlrTimers.TryGetValue(e.ConceptId, out var hlr))
            HlrTimers[e.ConceptId] = hlr with { HalfLifeHours = e.HalfLifeHours };

        EventVersion++;
    }

    public void Apply(MethodologySwitched_V1 e)
    {
        if (Enum.TryParse<Methodology>(e.NewMethodology, true, out var methodology))
            MethodologyMap[e.ConceptId] = methodology;

        var clusterKey = e.ConceptId;
        if (!MethodAttemptHistory.ContainsKey(clusterKey))
            MethodAttemptHistory[clusterKey] = new();

        MethodAttemptHistory[clusterKey].Add(new MethodologyAttemptRecord(
            e.NewMethodology, e.Trigger, e.StagnationScore, DateTimeOffset.UtcNow));

        EventVersion++;
    }

    public void Apply(SessionStarted_V1 e)
    {
        SessionCount++;
        ActiveSessionId = e.SessionId;
        ExperimentCohort ??= e.ExperimentCohort;
        EventVersion++;
    }

    public void Apply(SessionEnded_V1 e)
    {
        ActiveSessionId = null;
        EventVersion++;
    }

    public void Apply(XpAwarded_V1 e)
    {
        TotalXp = e.TotalXp;
        EventVersion++;
    }

    public void Apply(StreakUpdated_V1 e)
    {
        CurrentStreak = e.CurrentStreak;
        LongestStreak = e.LongestStreak;
        LastActivityDate = e.LastActivityDate;
        EventVersion++;
    }

    public void Apply(AnnotationAdded_V1 e)
    {
        // No state mutation beyond version bump -- annotations stored in event log
        EventVersion++;
    }

    public void Apply(StagnationDetected_V1 e)
    {
        // Stagnation events are informational -- state is updated via methodology switch
        EventVersion++;
    }

    // =========================================================================
    // BASELINES -- recalculated on every attempt from trailing-20 window
    // Uses median computation for response time (robust to outliers)
    // and simple ratio for accuracy.
    // =========================================================================

    public void RecalculateBaselines()
    {
        if (RecentAttempts.Count == 0) return;

        // Accuracy: simple ratio of correct answers in the trailing window
        BaselineAccuracy = RecentAttempts.Count(a => a.IsCorrect) / (double)RecentAttempts.Count;

        // Response time: median of trailing-20 window (robust to outliers)
        var sortedRt = RecentAttempts
            .Select(a => (double)a.ResponseTimeMs)
            .OrderBy(x => x)
            .ToList();

        int count = sortedRt.Count;
        if (count == 0) return;

        BaselineResponseTimeMs = count % 2 == 0
            ? (sortedRt[count / 2 - 1] + sortedRt[count / 2]) / 2.0
            : sortedRt[count / 2];
    }

    // =========================================================================
    // MEMORY ESTIMATION
    // =========================================================================

    /// <summary>
    /// Estimated memory footprint in bytes. Called periodically to enforce budget.
    /// Accounts for dictionary entries, list items, string storage, and object overhead.
    /// </summary>
    public long EstimateMemoryBytes()
    {
        // Per-entry estimates based on object layout + pointer overhead:
        // MasteryMap: string key (~40 bytes avg) + double (8) + dict entry overhead (~48) = ~96 bytes
        // RecentAttempts: AttemptRecord with 4 strings (~160 bytes) + bools/ints (~32) = ~192 bytes
        // HlrTimers: string key (~40) + HlrState (16) + dict entry (~48) = ~104 bytes
        // MethodAttemptHistory: string key (~40) + List<> (~24 + N * record ~80) = ~120 per entry
        long masteryBytes = MasteryMap.Count * 96L;
        long attemptBytes = RecentAttempts.Count * 192L;
        long hlrBytes = HlrTimers.Count * 104L;
        long methodHistoryBytes = MethodAttemptHistory.Sum(kv => 120L + kv.Value.Count * 80L);
        long methodMapBytes = MethodologyMap.Count * 56L;

        // Base object overhead: StudentState fields, string refs, DateTimeOffsets
        const long baseOverhead = 2048;

        return masteryBytes + attemptBytes + hlrBytes
             + methodHistoryBytes + methodMapBytes + baseOverhead;
    }
}

// =============================================================================
// SUPPORTING RECORDS
// =============================================================================

public sealed record AttemptRecord(
    string ConceptId,
    bool IsCorrect,
    int ResponseTimeMs,
    string ErrorType,
    string MethodologyActive,
    DateTimeOffset Timestamp);

public sealed record MethodologyAttemptRecord(
    string Methodology,
    string Trigger,
    double StagnationScore,
    DateTimeOffset SwitchedAt);

public sealed record HlrState(
    double HalfLifeHours,
    DateTimeOffset LastReviewAt);
