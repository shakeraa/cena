using Cena.Actors.Events;
using Cena.Actors.Students;

namespace Cena.Actors.Tests.Students;

/// <summary>
/// Tests for StudentState event-sourced projections (Apply methods).
/// Covers ACT-025.3 findings and event replay correctness.
/// </summary>
public sealed class StudentStateTests
{
    private readonly StudentState _sut = new() { StudentId = "test-student" };

    // ── ConceptAttempted_V1 ──

    [Fact]
    public void Apply_ConceptAttempted_UpdatesMasteryMap()
    {
        var evt = MakeAttempt("concept-1", isCorrect: true, posteriorMastery: 0.45);

        _sut.Apply(evt);

        Assert.Equal(0.45, _sut.MasteryMap["concept-1"]);
    }

    [Fact]
    public void Apply_ConceptAttempted_IncrementsEventVersion()
    {
        int before = _sut.EventVersion;
        _sut.Apply(MakeAttempt("c1", true, 0.5));
        Assert.Equal(before + 1, _sut.EventVersion);
    }

    [Fact]
    public void Apply_ConceptAttempted_AddsToRecentAttempts()
    {
        _sut.Apply(MakeAttempt("c1", true, 0.5));
        Assert.Single(_sut.RecentAttempts);
        Assert.Equal("c1", _sut.RecentAttempts[0].ConceptId);
    }

    [Fact]
    public void Apply_ConceptAttempted_CircularBuffer_EvictsOldest()
    {
        for (int i = 0; i < 25; i++)
            _sut.Apply(MakeAttempt($"c-{i}", true, 0.5));

        Assert.Equal(StudentState.MaxRecentAttempts, _sut.RecentAttempts.Count);
        // Oldest 5 should have been evicted, first remaining should be c-5
        Assert.Equal("c-5", _sut.RecentAttempts[0].ConceptId);
    }

    [Fact]
    public void Apply_ConceptAttempted_UpdatesBaselines()
    {
        _sut.Apply(MakeAttempt("c1", true, 0.5, responseTimeMs: 3000));
        _sut.Apply(MakeAttempt("c2", false, 0.3, responseTimeMs: 7000));

        // Accuracy: 1 correct out of 2 = 0.5
        Assert.Equal(0.5, _sut.BaselineAccuracy);
        // Response time: median of [3000, 7000] = 5000
        Assert.Equal(5000.0, _sut.BaselineResponseTimeMs);
    }

    // ── ConceptMastered_V1 ──

    [Fact]
    public void Apply_ConceptMastered_SetsHlrTimer()
    {
        var evt = new ConceptMastered_V1(
            "test-student", "c1", "session-1", 0.87, 10, 3,
            "Socratic", 24.0, DateTimeOffset.UtcNow);

        _sut.Apply(evt);

        Assert.True(_sut.HlrTimers.ContainsKey("c1"));
        Assert.Equal(24.0, _sut.HlrTimers["c1"].HalfLifeHours);
    }

    // ── SessionStarted_V1 ──

    [Fact]
    public void Apply_SessionStarted_IncrementsSessionCount()
    {
        var evt = new SessionStarted_V1(
            "test-student", "session-1", "mobile", "1.0.0",
            "Socratic", "control", false, DateTimeOffset.UtcNow);

        _sut.Apply(evt);

        Assert.Equal(1, _sut.SessionCount);
        Assert.Equal("session-1", _sut.ActiveSessionId);
    }

    [Fact]
    public void Apply_SessionStarted_SetsExperimentCohortOnlyOnce()
    {
        _sut.Apply(new SessionStarted_V1(
            "s", "s1", "m", "1", "S", "cohort-A", false, DateTimeOffset.UtcNow));
        _sut.Apply(new SessionStarted_V1(
            "s", "s2", "m", "1", "S", "cohort-B", false, DateTimeOffset.UtcNow));

        Assert.Equal("cohort-A", _sut.ExperimentCohort);
    }

    // ── SessionEnded_V1 ──

    [Fact]
    public void Apply_SessionEnded_ClearsActiveSession()
    {
        _sut.ActiveSessionId = "session-1";
        var evt = new SessionEnded_V1(
            "test-student", "session-1", "completed", 15, 10, 8, 3000, 0.3);

        _sut.Apply(evt);

        Assert.Null(_sut.ActiveSessionId);
    }

    // ── MethodologySwitched_V1 ──

    [Fact]
    public void Apply_MethodologySwitched_UpdatesMethodologyMap()
    {
        var evt = new MethodologySwitched_V1(
            "test-student", "c1", "Socratic", "Feynman",
            "stagnation_detected", 0.8, "Conceptual", 0.75);

        _sut.Apply(evt);

        Assert.Equal(Methodology.Feynman, _sut.MethodologyMap["c1"]);
    }

    [Fact]
    public void Apply_MethodologySwitched_TracksHistory()
    {
        var evt = new MethodologySwitched_V1(
            "test-student", "c1", "Socratic", "Feynman",
            "stagnation", 0.8, "Conceptual", 0.7);

        _sut.Apply(evt);

        Assert.Single(_sut.MethodAttemptHistory["c1"]);
        Assert.Equal("Feynman", _sut.MethodAttemptHistory["c1"][0].Methodology);
    }

    // ── XpAwarded_V1 ──

    [Fact]
    public void Apply_XpAwarded_SetsTotalXp()
    {
        var evt = new XpAwarded_V1("test-student", 10, "exercise_correct", 50, "recall", 1);
        _sut.Apply(evt);
        Assert.Equal(50, _sut.TotalXp);
    }

    // ── StreakUpdated_V1 ──

    [Fact]
    public void Apply_StreakUpdated_UpdatesAllStreakFields()
    {
        var now = DateTimeOffset.UtcNow;
        var evt = new StreakUpdated_V1("test-student", 5, 10, now);

        _sut.Apply(evt);

        Assert.Equal(5, _sut.CurrentStreak);
        Assert.Equal(10, _sut.LongestStreak);
        Assert.Equal(now, _sut.LastActivityDate);
    }

    // ── Memory estimation ──

    [Fact]
    public void EstimateMemoryBytes_EmptyState_ReturnsBaseOverhead()
    {
        long bytes = _sut.EstimateMemoryBytes();
        Assert.True(bytes >= 2048, $"Empty state should be at least base overhead, got {bytes}");
    }

    [Fact]
    public void EstimateMemoryBytes_GrowsWithConceptCount()
    {
        long before = _sut.EstimateMemoryBytes();

        for (int i = 0; i < 100; i++)
            _sut.MasteryMap[$"concept-{i}"] = 0.5;

        long after = _sut.EstimateMemoryBytes();
        Assert.True(after > before);
    }

    // ── Helpers ──

    private static ConceptAttempted_V1 MakeAttempt(
        string conceptId, bool isCorrect, double posteriorMastery,
        int responseTimeMs = 3000)
    {
        return new ConceptAttempted_V1(
            "test-student", conceptId, "session-1",
            isCorrect, responseTimeMs, "q1", "MultipleChoice",
            "Socratic", "None", 0.3, posteriorMastery,
            0, false, "hash", 0, 0, false, DateTimeOffset.UtcNow);
    }
}
