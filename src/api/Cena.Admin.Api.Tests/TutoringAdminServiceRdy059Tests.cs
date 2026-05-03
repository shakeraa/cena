// =============================================================================
// Cena Platform — TutoringAdminService RDY-059 merge tests
//
// Covers the pure-function enrichment at TutoringAdminService.MergeAccuracyAndFocus.
// Exercises the 4 spec edge cases (answers+focus, answers-only, focus-only,
// neither) plus invariants: null-not-zero for fresh sessions, accuracy
// rounding, multi-student per-student aggregation.
// =============================================================================

using Cena.Actors.Projections;
using Cena.Actors.Tutoring;
using Cena.Api.Contracts.Admin.Tutoring;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests;

public class TutoringAdminServiceRdy059Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-19T12:00:00Z");

    private static TutoringSessionDocument TutDoc(
        string id, string studentId,
        DateTimeOffset startedAt, DateTimeOffset? endedAt = null) => new()
    {
        Id = id,
        StudentId = studentId,
        SessionId = id,
        ConceptId = "c-1",
        Subject = "Math",
        Methodology = "socratic",
        StartedAt = startedAt,
        EndedAt = endedAt,
        TotalTurns = 3,
    };

    private static TutoringSessionSummaryDto Summary(string id, string studentId) => new(
        Id: id, StudentId: studentId, StudentName: studentId, SessionId: id,
        ConceptId: "c-1", Subject: "Math", Methodology: "socratic", Status: "active",
        TurnCount: 3, DurationSeconds: 120, TokensUsed: 0,
        StartedAt: Now.AddMinutes(-10), EndedAt: null);

    private static LearningSessionQueueProjection Queue(
        string studentId, params (DateTime at, bool correct)[] answers) => new()
    {
        Id = $"q-{studentId}",
        StudentId = studentId,
        StartedAt = Now.AddHours(-1).UtcDateTime,
        AnsweredQuestions = answers.Select((a, i) => new QuestionHistory
        {
            QuestionId = $"q-{i}",
            AnsweredAt = a.at,
            IsCorrect = a.correct,
            TimeSpentSeconds = 5,
        }).ToList(),
    };

    private static FocusSessionRollupDocument Rollup(
        string studentId, DateTimeOffset date, float score) => new()
    {
        Id = $"{studentId}:{date:yyyy-MM-dd}",
        StudentId = studentId,
        StudentName = studentId,
        SchoolId = "school-1",
        Date = date,
        AvgFocusScore = score,
        SessionCount = 1,
    };

    [Fact]
    public void BothSignals_Present_DtoCarriesAll()
    {
        // Session window: 10 minutes before Now. 3 attempts inside, 2 correct.
        var doc = TutDoc("sess-1", "stu-a", Now.AddMinutes(-10));
        var answers = new[]
        {
            (Now.AddMinutes(-8).UtcDateTime, true),
            (Now.AddMinutes(-6).UtcDateTime, true),
            (Now.AddMinutes(-4).UtcDateTime, false),
        };
        var queue = Queue("stu-a", answers);
        var rollup = Rollup("stu-a", Now.AddDays(-1), 82.5f);

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: new[] { queue },
            rollups: new[] { rollup },
            now: Now);

        Assert.Single(result);
        var r = result[0];
        Assert.Equal(3, r.QuestionsAnswered);
        Assert.Equal(66.7f, r.AccuracyPercent);   // 2/3 rounded to 1dp
        Assert.Equal(82.5f, r.FocusScore);
    }

    [Fact]
    public void AnswersOnly_FocusScoreIsNull()
    {
        var doc = TutDoc("sess-1", "stu-a", Now.AddMinutes(-10));
        var queue = Queue("stu-a", (Now.AddMinutes(-5).UtcDateTime, true));

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: new[] { queue },
            rollups: Array.Empty<FocusSessionRollupDocument>(),
            now: Now);

        var r = result[0];
        Assert.Equal(1, r.QuestionsAnswered);
        Assert.Equal(100f, r.AccuracyPercent);
        Assert.Null(r.FocusScore);
    }

    [Fact]
    public void FocusOnly_AnswersFieldsAreNull()
    {
        var doc = TutDoc("sess-1", "stu-a", Now.AddMinutes(-10));
        var rollup = Rollup("stu-a", Now.AddDays(-1), 70f);

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: Array.Empty<LearningSessionQueueProjection>(),
            rollups: new[] { rollup },
            now: Now);

        var r = result[0];
        Assert.Null(r.QuestionsAnswered);
        Assert.Null(r.AccuracyPercent);
        Assert.Equal(70f, r.FocusScore);
    }

    [Fact]
    public void Neither_AllThreeFieldsNull()
    {
        // Spec invariant: a fresh session with zero data must return
        // null, not 0. UI renders "—" off null; 0 would lie.
        var doc = TutDoc("sess-1", "stu-a", Now.AddMinutes(-1));

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: Array.Empty<LearningSessionQueueProjection>(),
            rollups: Array.Empty<FocusSessionRollupDocument>(),
            now: Now);

        var r = result[0];
        Assert.Null(r.QuestionsAnswered);
        Assert.Null(r.AccuracyPercent);
        Assert.Null(r.FocusScore);
    }

    [Fact]
    public void AnswersOutsideSessionWindow_NotCounted()
    {
        var doc = TutDoc("sess-1", "stu-a",
            startedAt: Now.AddMinutes(-10), endedAt: Now.AddMinutes(-5));
        var answers = new[]
        {
            (Now.AddMinutes(-8).UtcDateTime, true),    // INSIDE window
            (Now.AddMinutes(-2).UtcDateTime, true),    // AFTER EndedAt → excluded
            (Now.AddMinutes(-20).UtcDateTime, true),   // BEFORE StartedAt → excluded
        };
        var queue = Queue("stu-a", answers);

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: new[] { queue },
            rollups: Array.Empty<FocusSessionRollupDocument>(),
            now: Now);

        Assert.Equal(1, result[0].QuestionsAnswered);
    }

    [Fact]
    public void MultipleStudents_PerStudentAggregation()
    {
        var docA = TutDoc("s1", "stu-a", Now.AddMinutes(-10));
        var docB = TutDoc("s2", "stu-b", Now.AddMinutes(-10));

        var queueA = Queue("stu-a",
            (Now.AddMinutes(-8).UtcDateTime, true),
            (Now.AddMinutes(-7).UtcDateTime, true));    // 100% accuracy
        var queueB = Queue("stu-b",
            (Now.AddMinutes(-9).UtcDateTime, false),
            (Now.AddMinutes(-6).UtcDateTime, true));    // 50% accuracy

        var rollupA = Rollup("stu-a", Now.AddDays(-1), 90f);
        var rollupB = Rollup("stu-b", Now.AddDays(-1), 60f);

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("s1", "stu-a"), Summary("s2", "stu-b") },
            allDocs: new[] { docA, docB },
            queues: new[] { queueA, queueB },
            rollups: new[] { rollupA, rollupB },
            now: Now);

        Assert.Equal(2, result.Count);
        Assert.Equal(100f, result[0].AccuracyPercent);
        Assert.Equal(90f, result[0].FocusScore);
        Assert.Equal(50f, result[1].AccuracyPercent);
        Assert.Equal(60f, result[1].FocusScore);
    }

    [Fact]
    public void MultipleRollupsPerStudent_LatestByDateWins()
    {
        var doc = TutDoc("sess-1", "stu-a", Now.AddMinutes(-10));
        var old = Rollup("stu-a", Now.AddDays(-7), 50f);
        var recent = Rollup("stu-a", Now.AddDays(-1), 85f);

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: Array.Empty<LearningSessionQueueProjection>(),
            rollups: new[] { old, recent },  // unordered input
            now: Now);

        Assert.Equal(85f, result[0].FocusScore);
    }

    [Fact]
    public void EmptyPage_ReturnsEmpty_NeverCallsBelow()
    {
        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: Array.Empty<TutoringSessionSummaryDto>(),
            allDocs: Array.Empty<TutoringSessionDocument>(),
            queues: Array.Empty<LearningSessionQueueProjection>(),
            rollups: Array.Empty<FocusSessionRollupDocument>(),
            now: Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ActiveSession_NoEndedAt_UsesNowAsWindowUpperBound()
    {
        // Session still active (EndedAt=null). Window upper bound should
        // be the injected `now`, so an answer 2 minutes before now is
        // inside the window.
        var doc = TutDoc("sess-1", "stu-a", Now.AddMinutes(-30), endedAt: null);
        var queue = Queue("stu-a", (Now.AddMinutes(-2).UtcDateTime, true));

        var result = TutoringAdminService.MergeAccuracyAndFocus(
            paged: new[] { Summary("sess-1", "stu-a") },
            allDocs: new[] { doc },
            queues: new[] { queue },
            rollups: Array.Empty<FocusSessionRollupDocument>(),
            now: Now);

        Assert.Equal(1, result[0].QuestionsAnswered);
    }
}
