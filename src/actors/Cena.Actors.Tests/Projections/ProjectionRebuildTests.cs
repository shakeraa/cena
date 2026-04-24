// =============================================================================
// Cena Platform — Projection Rebuild Tests (RDY-021)
// Verifies that projections produce correct state when rebuilt from event streams.
//
// Simulates: delete projection state → replay all events → assert final state.
// This validates that projections can be safely rebuilt during maintenance
// or after schema changes.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Projections;

namespace Cena.Actors.Tests.Projections;

public class ProjectionRebuildTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

    // ═════════════════════════════════════════════════════════════════════════
    // StudentLifetimeStatsProjection — full rebuild from event stream
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void StudentLifetimeStats_RebuildFromEvents_ProducesCorrectState()
    {
        var projection = new StudentLifetimeStatsProjection();

        // Simulate event stream for one student
        var sessionStart = new LearningSessionStarted_V1(
            StudentId: "student-1",
            SessionId: "session-1",
            Subjects: new[] { "math" },
            Mode: "practice",
            DurationMinutes: 15,
            StartedAt: T0);

        var attempt1 = new ConceptAttempted_V1(
            StudentId: "student-1", ConceptId: "algebra-1", SessionId: "session-1",
            IsCorrect: true, ResponseTimeMs: 4000, QuestionId: "q1",
            QuestionType: "mcq", MethodologyActive: "adaptive", ErrorType: "",
            PriorMastery: 0.3, PosteriorMastery: 0.4, HintCountUsed: 0,
            WasSkipped: false, AnswerHash: "a1", BackspaceCount: 0,
            AnswerChangeCount: 0, WasOffline: false, Timestamp: T0.AddMinutes(1));

        var attempt2 = new ConceptAttempted_V1(
            StudentId: "student-1", ConceptId: "algebra-1", SessionId: "session-1",
            IsCorrect: false, ResponseTimeMs: 6000, QuestionId: "q2",
            QuestionType: "mcq", MethodologyActive: "adaptive", ErrorType: "computation",
            PriorMastery: 0.4, PosteriorMastery: 0.35, HintCountUsed: 1,
            WasSkipped: false, AnswerHash: "a2", BackspaceCount: 3,
            AnswerChangeCount: 1, WasOffline: false, Timestamp: T0.AddMinutes(2));

        var attempt3 = new ConceptAttempted_V2(
            StudentId: "student-1", ConceptId: "geometry-1", SessionId: "session-1",
            IsCorrect: true, ResponseTimeMs: 3000, QuestionId: "q3",
            QuestionType: "open", MethodologyActive: "adaptive", ErrorType: "",
            PriorMastery: 0.5, PosteriorMastery: 0.6, HintCountUsed: 0,
            WasSkipped: false, AnswerHash: "a3", BackspaceCount: 0,
            AnswerChangeCount: 0, WasOffline: false, Timestamp: T0.AddMinutes(3),
            Duration: TimeSpan.FromSeconds(10));

        var challenge = new ChallengeCompleted_V1(
            StudentId: "student-1", ChallengeId: "daily-1",
            ChallengeName: "Daily", IsBoss: false, Score: 90,
            CompletedAt: T0.AddMinutes(5));

        var badge = new BadgeEarned_V1(
            StudentId: "student-1", BadgeId: "first-session",
            BadgeName: "First Steps", AwardedAt: T0.AddMinutes(6));

        // Rebuild: Create from first event, then apply the rest
        var stats = projection.Create(sessionStart);

        projection.Apply(attempt1, stats);
        projection.Apply(attempt2, stats);
        projection.Apply(attempt3, stats);
        projection.Apply(challenge, stats);
        projection.Apply(badge, stats);

        // Assert final state matches expected
        Assert.Equal("student-1", stats.StudentId);
        Assert.Equal(1, stats.TotalSessions);
        Assert.Equal(3, stats.TotalAttempts);
        Assert.Equal(2, stats.TotalCorrect);
        Assert.Equal(2.0 / 3.0, stats.Accuracy, 4);
        Assert.Equal(1, stats.ChallengesCompleted);
        Assert.Equal(0, stats.BossesDefeated);
        Assert.Equal(1, stats.BadgeCount);
        Assert.Contains("first-session", stats.BadgeIds);
        Assert.Equal(1, stats.CurrentStreak);
        Assert.Equal(1, stats.LongestStreak);
        Assert.Equal(T0.AddMinutes(6), stats.UpdatedAt);
    }

    [Fact]
    public void StudentLifetimeStats_RebuildWithMultipleSessions_TracksStreaks()
    {
        var projection = new StudentLifetimeStatsProjection();

        // Day 1
        var session1 = new LearningSessionStarted_V1(
            StudentId: "student-1", SessionId: "s1",
            Subjects: new[] { "math" }, Mode: "practice",
            DurationMinutes: 10, StartedAt: T0);

        // Day 2 (consecutive)
        var session2 = new LearningSessionStarted_V1(
            StudentId: "student-1", SessionId: "s2",
            Subjects: new[] { "math" }, Mode: "practice",
            DurationMinutes: 10, StartedAt: T0.AddDays(1));

        // Day 3 (consecutive)
        var session3 = new LearningSessionStarted_V1(
            StudentId: "student-1", SessionId: "s3",
            Subjects: new[] { "math" }, Mode: "practice",
            DurationMinutes: 10, StartedAt: T0.AddDays(2));

        // Day 10 (gap — streak breaks)
        var session4 = new LearningSessionStarted_V1(
            StudentId: "student-1", SessionId: "s4",
            Subjects: new[] { "math" }, Mode: "practice",
            DurationMinutes: 10, StartedAt: T0.AddDays(9));

        var stats = projection.Create(session1);
        projection.Apply(session2, stats);
        projection.Apply(session3, stats);
        projection.Apply(session4, stats);

        Assert.Equal(4, stats.TotalSessions);
        Assert.Equal(3, stats.LongestStreak); // 3-day streak (Day 1-3)
        Assert.Equal(1, stats.CurrentStreak); // Reset after gap
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SessionAttemptHistoryProjection — rebuild from event stream
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionAttemptHistory_RebuildFromEvents_ProducesCorrectState()
    {
        var projection = new SessionAttemptHistoryProjection();
        var doc = new SessionAttemptHistoryDocument();

        var attempt1 = new ConceptAttempted_V1(
            StudentId: "student-1", ConceptId: "algebra-1", SessionId: "session-1",
            IsCorrect: true, ResponseTimeMs: 4000, QuestionId: "q1",
            QuestionType: "mcq", MethodologyActive: "adaptive", ErrorType: "",
            PriorMastery: 0.3, PosteriorMastery: 0.4, HintCountUsed: 0,
            WasSkipped: false, AnswerHash: "a1", BackspaceCount: 0,
            AnswerChangeCount: 0, WasOffline: false, Timestamp: T0.AddMinutes(1));

        var attempt2 = new ConceptAttempted_V2(
            StudentId: "student-1", ConceptId: "algebra-1", SessionId: "session-1",
            IsCorrect: false, ResponseTimeMs: 6000, QuestionId: "q2",
            QuestionType: "mcq", MethodologyActive: "adaptive", ErrorType: "computation",
            PriorMastery: 0.4, PosteriorMastery: 0.35, HintCountUsed: 1,
            WasSkipped: false, AnswerHash: "a2", BackspaceCount: 3,
            AnswerChangeCount: 1, WasOffline: false, Timestamp: T0.AddMinutes(2),
            Duration: TimeSpan.FromSeconds(12));

        var attempt3 = new ConceptAttempted_V1(
            StudentId: "student-1", ConceptId: "geometry-1", SessionId: "session-1",
            IsCorrect: true, ResponseTimeMs: 3000, QuestionId: "q3",
            QuestionType: "open", MethodologyActive: "adaptive", ErrorType: "",
            PriorMastery: 0.5, PosteriorMastery: 0.6, HintCountUsed: 0,
            WasSkipped: false, AnswerHash: "a3", BackspaceCount: 0,
            AnswerChangeCount: 0, WasOffline: false, Timestamp: T0.AddMinutes(3));

        // Rebuild from scratch
        projection.Apply(attempt1, doc);
        projection.Apply(attempt2, doc);
        projection.Apply(attempt3, doc);

        Assert.Equal("session-1", doc.SessionId);
        Assert.Equal("student-1", doc.StudentId);
        Assert.Equal(3, doc.TotalAttempts);
        Assert.Equal(2, doc.CorrectAttempts);
        Assert.Equal(2.0 / 3.0, doc.Accuracy, 4);

        // Mastery deltas: algebra-1 went 0.3→0.35, geometry-1 went 0.5→0.6
        Assert.Equal(2, doc.MasteryDeltas.Count);
        Assert.Equal(0.05, doc.MasteryDeltas["algebra-1"], 4); // 0.35 - 0.3
        Assert.Equal(0.10, doc.MasteryDeltas["geometry-1"], 4); // 0.6 - 0.5
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FeatureFlagProjection — rebuild from event stream
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FeatureFlag_RebuildFromEvents_ReflectsLatestState()
    {
        var projection = new FeatureFlagProjection();
        var doc = new FeatureFlagDocument();

        var set1 = new FeatureFlagSet_V1(
            FlagName: "dark-mode", Enabled: false, RolloutPercent: 0,
            SetByUserId: "admin-1", Reason: "Initial", Timestamp: T0);

        var set2 = new FeatureFlagSet_V1(
            FlagName: "dark-mode", Enabled: true, RolloutPercent: 25.0,
            SetByUserId: "admin-2", Reason: "Beta rollout", Timestamp: T0.AddDays(1));

        var set3 = new FeatureFlagSet_V1(
            FlagName: "dark-mode", Enabled: true, RolloutPercent: 100.0,
            SetByUserId: "admin-1", Reason: "Full rollout", Timestamp: T0.AddDays(7));

        projection.Apply(set1, doc);
        projection.Apply(set2, doc);
        projection.Apply(set3, doc);

        Assert.Equal("dark-mode", doc.Name);
        Assert.True(doc.Enabled);
        Assert.Equal(100.0, doc.RolloutPercent);
        Assert.Equal("admin-1", doc.UpdatedBy);
        Assert.Equal("Full rollout", doc.Reason);
        Assert.Equal(3, doc.History.Count);
        Assert.False(doc.IsDeleted);
    }

    [Fact]
    public void FeatureFlag_RebuildWithDelete_MarksDeleted()
    {
        var projection = new FeatureFlagProjection();
        var doc = new FeatureFlagDocument();

        var set = new FeatureFlagSet_V1(
            FlagName: "experiment-1", Enabled: true, RolloutPercent: 10.0,
            SetByUserId: "admin-1", Reason: "Testing", Timestamp: T0);

        var delete = new FeatureFlagDeleted_V1(
            FlagName: "experiment-1", DeletedByUserId: "admin-1",
            Timestamp: T0.AddDays(30));

        projection.Apply(set, doc);
        projection.Apply(delete, doc);

        Assert.True(doc.IsDeleted);
        Assert.Equal(T0.AddDays(30), doc.UpdatedAt);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Empty stream rebuild — projections handle zero events gracefully
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionAttemptHistory_EmptyStream_ProducesDefaultState()
    {
        var doc = new SessionAttemptHistoryDocument();

        Assert.Equal(0, doc.TotalAttempts);
        Assert.Equal(0, doc.CorrectAttempts);
        Assert.Equal(0, doc.Accuracy);
        Assert.Empty(doc.Attempts);
        Assert.Empty(doc.MasteryDeltas);
    }

    [Fact]
    public void StudentLifetimeStats_DefaultState_HasZeroCounters()
    {
        var stats = new StudentLifetimeStats();

        Assert.Equal(0, stats.TotalSessions);
        Assert.Equal(0, stats.TotalAttempts);
        Assert.Equal(0, stats.TotalCorrect);
        Assert.Equal(0, stats.Accuracy);
        Assert.Equal(0, stats.ChallengesCompleted);
        Assert.Equal(0, stats.BossesDefeated);
        Assert.Equal(0, stats.BadgeCount);
        Assert.Empty(stats.BadgeIds);
    }
}
