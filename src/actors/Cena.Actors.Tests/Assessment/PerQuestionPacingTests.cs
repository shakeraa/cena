// =============================================================================
// Cena Platform — Per-question pacing computation tests (PRR-299)
//
// Pins the pure-function pacing-overlay logic in
// MockExamGrader.OverlayPerQuestionPacing. Real-Postgres exercise of the
// SubmitAnswer → state.AnswerTimestamps round-trip lives in the integration
// suite; these tests pin the deterministic transform.
//
// Coordinator framing (m_f578dc757ac8): pacing is a diagnostic, NOT a
// streak / loss-aversion mechanic. Tests assert positive-only TimeSpent
// (no negative-time UX), null-when-unanswered, and the chain ordering
// from the spec ("time spent on Q[i] = first(Q[i]) − last(prior Q)").
// =============================================================================

using Cena.Actors.Assessment;

namespace Cena.Actors.Tests.Assessment;

public sealed class PerQuestionPacingTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private static ExamSimulationState BuildState(
        DateTimeOffset startedAt,
        params (string Key, DateTimeOffset First, DateTimeOffset Last)[] answers)
    {
        var s = new ExamSimulationState
        {
            SimulationId = "run-pacing-test",
            StudentId = "student-x",
            ExamCode = "806",
            StartedAt = startedAt,
        };
        foreach (var (k, first, last) in answers)
        {
            s.AnswerTimestamps[k] = first;
            s.AnswerLastTimestamps[k] = last;
        }
        return s;
    }

    private static MockExamPerQuestionResult BuildPq(
        string qid, IReadOnlyList<MockExamSubpartResult>? subparts = null) =>
        new(
            QuestionId: qid,
            Section: "A",
            Attempted: true,
            Correct: true,
            StudentAnswer: "x",
            CanonicalAnswer: "x",
            GradingEngine: "test",
            Points: 25,
            PointsAwarded: 25,
            Subparts: subparts);

    [Fact]
    public void Overlay_NoAnswers_All_Pacing_Fields_Null()
    {
        var state = BuildState(T0);
        var pq = new[] { BuildPq("q1"), BuildPq("q2") };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.All(result, r =>
        {
            Assert.Null(r.FirstAnsweredAt);
            Assert.Null(r.TimeSpent);
        });
    }

    [Fact]
    public void Overlay_FirstAnsweredAt_Is_MinTimestamp_Across_AnswerKeys()
    {
        // Multi-part: subpart "a" answered at T0+5min; subpart "b" answered at T0+8min.
        // FirstAnsweredAt should be T0+5.
        var state = BuildState(
            T0,
            ("q1:a", T0.AddMinutes(5), T0.AddMinutes(5)),
            ("q1:b", T0.AddMinutes(8), T0.AddMinutes(9)));

        var subparts = new MockExamSubpartResult[]
        {
            new("a", true, true, "x", "x", "test", 25, 25),
            new("b", true, true, "y", "y", "test", 25, 25),
        };
        var pq = new[] { BuildPq("q1", subparts) };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.Equal(T0.AddMinutes(5), result[0].FirstAnsweredAt);
    }

    [Fact]
    public void Overlay_TimeSpent_FirstQuestion_Equals_FirstMinusStartedAt()
    {
        // Q1 first answered at T0+10min ⇒ TimeSpent(Q1) = 10 min
        // (the warmup + reading time before the student's first commit).
        var state = BuildState(T0, ("q1", T0.AddMinutes(10), T0.AddMinutes(10)));
        var pq = new[] { BuildPq("q1") };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.Equal(TimeSpan.FromMinutes(10), result[0].TimeSpent);
    }

    [Fact]
    public void Overlay_TimeSpent_SecondQuestion_Equals_FirstMinusPriorLast()
    {
        // Spec: time spent on Q[i] = first(Q[i]) − last(prior Q).
        // Q1: first T0+10, last T0+15.   ⇒ TimeSpent(Q1) = 10
        // Q2: first T0+18, last T0+20.   ⇒ TimeSpent(Q2) = 18 − 15 = 3
        var state = BuildState(
            T0,
            ("q1", T0.AddMinutes(10), T0.AddMinutes(15)),
            ("q2", T0.AddMinutes(18), T0.AddMinutes(20)));
        var pq = new[] { BuildPq("q1"), BuildPq("q2") };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.Equal(TimeSpan.FromMinutes(10), result[0].TimeSpent);
        Assert.Equal(TimeSpan.FromMinutes(3), result[1].TimeSpent);
    }

    [Fact]
    public void Overlay_RebuildOrder_Preserves_Original_SlotOrder()
    {
        // Out-of-order: student first answers Q2 (at T0+5), then Q1 (at T0+12).
        // The rebuilt perQuestion list MUST stay in slot order (Q1, Q2),
        // not in answer order — the SPA renders the result page slot-first.
        var state = BuildState(
            T0,
            ("q1", T0.AddMinutes(12), T0.AddMinutes(15)),
            ("q2", T0.AddMinutes(5), T0.AddMinutes(8)));
        var pq = new[] { BuildPq("q1"), BuildPq("q2") };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.Equal("q1", result[0].QuestionId);
        Assert.Equal("q2", result[1].QuestionId);
        // Sanity: Q2 was the first to be answered, so its TimeSpent uses
        // StartedAt as anchor (5 min). Q1 followed; its TimeSpent uses
        // Q2's lastAnsweredAt (T0+8) as anchor → 12 − 8 = 4 min.
        Assert.Equal(TimeSpan.FromMinutes(5), result[1].TimeSpent);
        Assert.Equal(TimeSpan.FromMinutes(4), result[0].TimeSpent);
    }

    [Fact]
    public void Overlay_NegativeTimeSpent_ClampsToZero()
    {
        // Defensive: a clock-skew or buggy bulk-submit could yield
        // "Q's first" earlier than "prior Q's last". Negative TimeSpent
        // would confuse the SPA renderer; clamp to zero.
        // Q1 first T0+10, last T0+30 (long edits).
        // Q2 first T0+15  → 15 − 30 = -15 → clamp 0.
        var state = BuildState(
            T0,
            ("q1", T0.AddMinutes(10), T0.AddMinutes(30)),
            ("q2", T0.AddMinutes(15), T0.AddMinutes(15)));
        var pq = new[] { BuildPq("q1"), BuildPq("q2") };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.True(result[1].TimeSpent! >= TimeSpan.Zero);
    }

    [Fact]
    public void Overlay_AnsweredAndUnanswered_Mix_OnlyAnswered_GetPacing()
    {
        // Q1 answered, Q2 not answered, Q3 answered.
        // Pacing chain only walks {Q1, Q3} sorted by firstAnswered.
        // Q1: first 5 → TimeSpent = 5
        // Q2: not answered → null
        // Q3: first 12, prior Q1 lastAt 10 → TimeSpent = 2
        var state = BuildState(
            T0,
            ("q1", T0.AddMinutes(5), T0.AddMinutes(10)),
            ("q3", T0.AddMinutes(12), T0.AddMinutes(13)));
        var pq = new[] { BuildPq("q1"), BuildPq("q2"), BuildPq("q3") };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.Equal(TimeSpan.FromMinutes(5), result[0].TimeSpent);
        Assert.Null(result[1].TimeSpent);
        Assert.Null(result[1].FirstAnsweredAt);
        Assert.Equal(TimeSpan.FromMinutes(2), result[2].TimeSpent);
    }

    [Fact]
    public void Overlay_DefensiveAgainstNullSubparts()
    {
        // Phase-2A subpart-less rows must not throw.
        var state = BuildState(T0, ("q1", T0.AddMinutes(3), T0.AddMinutes(4)));
        var pq = new[] { BuildPq("q1", subparts: null) };

        var result = MockExamGrader.OverlayPerQuestionPacing(pq, state);

        Assert.Equal(T0.AddMinutes(3), result[0].FirstAnsweredAt);
        Assert.Equal(TimeSpan.FromMinutes(3), result[0].TimeSpent);
    }
}
