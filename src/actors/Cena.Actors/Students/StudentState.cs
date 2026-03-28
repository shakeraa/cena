// =============================================================================
// Cena Platform -- StudentState (Event-Sourced Aggregate State)
// Layer: Actor Model | Runtime: .NET 9
//
// In-memory state for a single student actor. Rebuilt from Marten snapshot +
// event replay on activation. Zero database round-trips for reads.
// Memory budget: ~500KB per actor instance.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Methodology;

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

    // ---- Hierarchical Methodology Maps (Subject → Topic → Concept cascade) ----
    /// <summary>Subject ID → methodology assignment with confidence tracking.</summary>
    public Dictionary<string, MethodologyAssignment> SubjectMethodologyMap { get; set; } = new();

    /// <summary>Topic ID → methodology assignment with confidence tracking.</summary>
    public Dictionary<string, MethodologyAssignment> TopicMethodologyMap { get; set; } = new();

    /// <summary>Concept ID → methodology assignment with confidence tracking (Layer 5).</summary>
    public Dictionary<string, MethodologyAssignment> ConceptMethodologyMap { get; set; } = new();

    /// <summary>Sessions since last methodology switch, per level key (for cooldown tracking).</summary>
    public Dictionary<string, int> SessionsSinceSwitch { get; set; } = new();

    // ---- Attempt History (sliding window for baselines) ----
    /// <summary>
    /// Last 20 attempts across all concepts -- used for baseline accuracy and
    /// response time. Circular buffer semantics: oldest evicted on overflow.
    /// </summary>
    public List<AttemptRecord> RecentAttempts { get; set; } = new(MaxRecentAttempts);

    // ---- Half-Life Regression Timers ----
    /// <summary>Concept ID -> HLR state (half-life in hours, last review time).</summary>
    public Dictionary<string, HlrState> HlrTimers { get; set; } = new();

    // ---- Rich Mastery Overlay (MST-006) ----
    /// <summary>
    /// Per-concept rich mastery state from the mastery engine pipeline.
    /// Includes BKT probability, HLR half-life, Bloom's level, error history,
    /// quality quadrant, and method tracking. Updated additively after each
    /// attempt alongside the existing MasteryMap.
    /// </summary>
    public Dictionary<string, Mastery.ConceptMasteryState> MasteryOverlay { get; set; } = new();

    /// <summary>
    /// Student's personal response time baseline for mastery quality classification.
    /// Tracks median response time from a circular buffer of last 20 attempts.
    /// </summary>
    public ResponseTimeBaseline ResponseBaseline { get; set; } = ResponseTimeBaseline.Initial;

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

        // MST-006: Update rich mastery overlay from event data
        ApplyMasteryOverlay(e);

        LastActivityDate = e.Timestamp;
        RecalculateBaselines();
        EventVersion++;
    }

    /// <summary>
    /// MST-006: Rebuild/update the rich mastery overlay from event data.
    /// Deterministic for event replay — uses event fields only, no external services.
    /// </summary>
    private void ApplyMasteryOverlay(ConceptAttempted_V1 e)
    {
        if (!MasteryOverlay.TryGetValue(e.ConceptId, out var current))
            current = new Mastery.ConceptMasteryState();

        // Update attempt counters and streak
        current = current.WithAttempt(e.IsCorrect, e.Timestamp);

        // Update mastery probability from event (BKT was already computed)
        current = current.WithBktUpdate((float)e.PosteriorMastery);

        // Update HLR half-life from HlrTimers if available
        if (HlrTimers.TryGetValue(e.ConceptId, out var hlr))
            current = current.WithHalfLifeUpdate((float)hlr.HalfLifeHours);

        // Track error type if present
        if (e.ErrorType != "None" && Enum.TryParse<Mastery.ErrorType>(e.ErrorType, true, out var errorType))
            current = current.WithRecentError(errorType);

        // Update quality quadrant from response time baseline
        var quality = MasteryQualityClassifier.Classify(
            e.IsCorrect, e.ResponseTimeMs, ResponseBaseline.MedianResponseTimeMs);
        current = current with { QualityQuadrant = quality };

        // Update response time baseline
        ResponseBaseline = ResponseBaseline.Update(e.ResponseTimeMs);

        MasteryOverlay[e.ConceptId] = current;
    }

    public void Apply(ConceptMastered_V1 e)
    {
        MasteryMap[e.ConceptId] = e.MasteryLevel;
        HlrTimers[e.ConceptId] = new HlrState(e.InitialHalfLifeHours, e.Timestamp);

        // MST-006: Update overlay with half-life on mastery
        if (MasteryOverlay.TryGetValue(e.ConceptId, out var state))
            MasteryOverlay[e.ConceptId] = state.WithHalfLifeUpdate((float)e.InitialHalfLifeHours);

        EventVersion++;
    }

    public void Apply(MasteryDecayed_V1 e)
    {
        if (MasteryMap.ContainsKey(e.ConceptId))
            MasteryMap[e.ConceptId] = e.PredictedRecall;

        if (HlrTimers.TryGetValue(e.ConceptId, out var hlr))
            HlrTimers[e.ConceptId] = hlr with { HalfLifeHours = e.HalfLifeHours };

        // MST-006: Update overlay mastery probability on decay
        if (MasteryOverlay.TryGetValue(e.ConceptId, out var state))
            MasteryOverlay[e.ConceptId] = state.WithBktUpdate((float)e.PredictedRecall)
                .WithHalfLifeUpdate((float)e.HalfLifeHours);

        EventVersion++;
    }

    public void Apply(MethodologySwitched_V1 e)
    {
        if (Enum.TryParse<Methodology>(e.NewMethodology, true, out var methodology))
            MethodologyMap[e.ConceptId] = methodology;

        var clusterKey = e.ConceptId;
        if (!MethodAttemptHistory.ContainsKey(clusterKey))
            MethodAttemptHistory[clusterKey] = new();

        // ACT-028: Use event timestamp for deterministic replay — never wall clock
        MethodAttemptHistory[clusterKey].Add(new MethodologyAttemptRecord(
            e.NewMethodology, e.Trigger, e.StagnationScore, e.Timestamp));

        EventVersion++;
    }

    public void Apply(SessionStarted_V1 e)
    {
        SessionCount++;
        ActiveSessionId = e.SessionId;
        ExperimentCohort ??= e.ExperimentCohort;
        IncrementSessionsSinceSwitch();
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

    public void Apply(MethodologyConfidenceReached_V1 e)
    {
        // Informational — confidence milestone logged in event stream
        EventVersion++;
    }

    public void Apply(MethodologySwitchDeferred_V1 e)
    {
        // Informational — cooldown deferral logged in event stream
        EventVersion++;
    }

    public void Apply(TeacherMethodologyOverride_V1 e)
    {
        var level = Enum.TryParse<MethodologyLevel>(e.Level, true, out var l)
            ? l : MethodologyLevel.Concept;
        var methodology = Enum.TryParse<Methodology>(e.ToMethodology, true, out var m)
            ? m : Methodology.Socratic;

        var assignment = MethodologyAssignment.Default(methodology, MethodologySource.TeacherOverride)
            with { LastSwitchAt = e.Timestamp };

        switch (level)
        {
            case MethodologyLevel.Subject:
                SubjectMethodologyMap[e.LevelId] = assignment;
                break;
            case MethodologyLevel.Topic:
                TopicMethodologyMap[e.LevelId] = assignment;
                break;
            case MethodologyLevel.Concept:
                ConceptMethodologyMap[e.LevelId] = assignment;
                // Keep existing flat map in sync
                MethodologyMap[e.LevelId] = methodology;
                break;
        }
        EventVersion++;
    }

    /// <summary>
    /// Update hierarchical methodology tracking after a concept attempt.
    /// Aggregates attempt data at concept, topic, and subject levels.
    /// </summary>
    public void UpdateMethodologyHierarchy(
        string conceptId, string? topicId, string? subjectId,
        bool isCorrect, string methodologyActive)
    {
        if (!Enum.TryParse<Methodology>(methodologyActive, true, out var methodology))
            return;

        // Update concept-level assignment
        UpdateAssignmentAttempt(ConceptMethodologyMap, conceptId, methodology, isCorrect);

        // Update topic-level assignment
        if (topicId != null)
            UpdateAssignmentAttempt(TopicMethodologyMap, topicId, methodology, isCorrect);

        // Update subject-level assignment
        if (subjectId != null)
            UpdateAssignmentAttempt(SubjectMethodologyMap, subjectId, methodology, isCorrect);
    }

    private static void UpdateAssignmentAttempt(
        Dictionary<string, MethodologyAssignment> map,
        string key,
        Methodology methodology,
        bool isCorrect)
    {
        if (map.TryGetValue(key, out var existing) && existing.Methodology == methodology)
        {
            map[key] = existing.WithAttempt(isCorrect);
        }
        else if (!map.ContainsKey(key))
        {
            // First attempt at this level — seed with current methodology
            map[key] = MethodologyAssignment.Default(methodology, MethodologySource.McmRouted)
                .WithAttempt(isCorrect);
        }
        // If the methodology doesn't match the current assignment, don't update
        // (the student is using a different method than what's assigned — could be a transition)
    }

    /// <summary>
    /// Increment session counters for cooldown tracking across all levels.
    /// Called on SessionStarted.
    /// </summary>
    public void IncrementSessionsSinceSwitch()
    {
        var keys = SessionsSinceSwitch.Keys.ToList();
        foreach (var key in keys)
            SessionsSinceSwitch[key] = SessionsSinceSwitch[key] + 1;
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
        long hierarchyBytes = (SubjectMethodologyMap.Count + TopicMethodologyMap.Count
            + ConceptMethodologyMap.Count) * 128L
            + SessionsSinceSwitch.Count * 48L;

        // MST-006: MasteryOverlay — ConceptMasteryState record (~256 bytes) + arrays
        long overlayBytes = MasteryOverlay.Sum(kv =>
            96L + 256L + (kv.Value.RecentErrors.Length * 4L) + (kv.Value.MethodHistory.Length * 80L));
        // ResponseBaseline: ~(20 * 4) + 16 overhead
        long baselineBytes = 96L + (ResponseBaseline.ResponseTimes.Length * 4L);

        // Base object overhead: StudentState fields, string refs, DateTimeOffsets
        const long baseOverhead = 2048;

        return masteryBytes + attemptBytes + hlrBytes
             + methodHistoryBytes + methodMapBytes + hierarchyBytes
             + overlayBytes + baselineBytes + baseOverhead;
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
