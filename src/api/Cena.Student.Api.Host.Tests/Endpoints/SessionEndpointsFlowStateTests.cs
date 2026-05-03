// =============================================================================
// Cena Platform — RDY-034 slice 2 tests
// SessionEndpoints.ComputeSessionFlowState: flow-state assessment computed
// from the session attempt-history projection. Covered here rather than
// the full HTTP harness because the helper is the only behavior slice 2
// introduces — the handler just calls it and drops the result into the DTO.
//
// Uses the real FlowStateService + real CognitiveLoadService (no mocks):
// both are pure, deterministic, and cheap to instantiate. Swapping them
// for stubs would hide regressions in the cognitive-load formula + state
// machine — the very things slice 2 is exposing.
// =============================================================================

using Cena.Actors.Projections;
using Cena.Actors.Services;
using Cena.Api.Host.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class SessionEndpointsFlowStateTests
{
    // ── Brand-new sessions ───────────────────────────────────────────────

    [Fact]
    public void ComputeSessionFlowState_NullHistory_ReturnsWarmingZeroFatigue()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);

        var (assessment, fatigue) = SessionEndpoints.ComputeSessionFlowState(
            history: null,
            sessionStartedAt: started,
            sessionEndedAt: null,
            flowState: flow,
            cognitiveLoad: cognitive);

        Assert.Equal(FlowStateKind.Warming, assessment.State);
        Assert.Equal(0, assessment.ConsecutiveCorrect);
        Assert.Equal(0.0, fatigue);
    }

    [Fact]
    public void ComputeSessionFlowState_EmptyAttemptList_ReturnsWarmingZeroFatigue()
    {
        var (flow, cognitive) = CreateServices();
        var history = new SessionAttemptHistoryDocument
        {
            Id = "session-1",
            SessionId = "session-1",
            StudentId = "student-1",
        };
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);

        var (assessment, fatigue) = SessionEndpoints.ComputeSessionFlowState(
            history: history,
            sessionStartedAt: started,
            sessionEndedAt: null,
            flowState: flow,
            cognitiveLoad: cognitive);

        Assert.Equal(FlowStateKind.Warming, assessment.State);
        Assert.Equal(0.0, fatigue);
    }

    // ── Streak + rising trend → inFlow ───────────────────────────────────

    [Fact]
    public void ComputeSessionFlowState_StreakAndRisingTrend_ClassifiesInFlow()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-8);

        // 2 wrong at the start (baseline pulled down), 5 right in the rolling
        // window → rolling-vs-baseline trend is strongly positive; fatigue is
        // low because RT is stable and the session is short.
        var history = BuildHistory(
            started,
            // baseline tail: 2 wrong, slow start
            (false, 3500),
            (false, 3800),
            // rolling 5: all correct, fast
            (true, 1400),
            (true, 1300),
            (true, 1250),
            (true, 1200),
            (true, 1150));

        var (assessment, fatigue) = SessionEndpoints.ComputeSessionFlowState(
            history, started, null, flow, cognitive);

        Assert.Equal(FlowStateKind.InFlow, assessment.State);
        Assert.Equal(5, assessment.ConsecutiveCorrect);
        // InFlow precondition: fatigue < 0.4 (FlowStateService.InFlowFatigueCeiling).
        // Hard-coded here because the constant is internal to Cena.Actors.
        Assert.True(fatigue < 0.4,
            $"fatigue={fatigue} should be below InFlow ceiling 0.4");
    }

    // ── Steep negative trend → disrupted ────────────────────────────────

    [Fact]
    public void ComputeSessionFlowState_DecliningAccuracy_ClassifiesDisrupted()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Baseline strong, last 5 fall apart → rolling−baseline ≈ −0.6
        var history = BuildHistory(
            started,
            // early: all correct
            (true, 1200),
            (true, 1250),
            (true, 1300),
            (true, 1150),
            (true, 1200),
            // last 5: mostly wrong
            (false, 2800),
            (false, 3100),
            (false, 2900),
            (true, 3000),
            (false, 3200));

        var (assessment, _) = SessionEndpoints.ComputeSessionFlowState(
            history, started, null, flow, cognitive);

        Assert.Equal(FlowStateKind.Disrupted, assessment.State);
        Assert.Equal(FlowStateAction.ReduceDifficulty, assessment.RecommendedAction);
    }

    // ── Long session → fatigued + cooldown populated ─────────────────────

    [Fact]
    public void ComputeSessionFlowState_LongSession_ClassifiesFatiguedWithCooldown()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-50);   // >45 cap

        var history = BuildHistory(
            started,
            (true, 1500),
            (true, 1600),
            (true, 1700));

        var (assessment, _) = SessionEndpoints.ComputeSessionFlowState(
            history, started, null, flow, cognitive);

        Assert.Equal(FlowStateKind.Fatigued, assessment.State);
        Assert.NotNull(assessment.CooldownMinutes);
        Assert.InRange(assessment.CooldownMinutes!.Value, 5, 30);
    }

    // ── Ended sessions use EndedAt, not UtcNow, for duration ─────────────

    [Fact]
    public void ComputeSessionFlowState_EndedSession_UsesEndedAtForDuration()
    {
        var (flow, cognitive) = CreateServices();
        // Session ran 5 minutes, ended hours ago.
        var started = DateTimeOffset.UtcNow.AddHours(-6).AddMinutes(-5);
        var ended = DateTimeOffset.UtcNow.AddHours(-6);

        var history = BuildHistory(
            started,
            (true, 1200),
            (true, 1250));

        var (assessment, _) = SessionEndpoints.ComputeSessionFlowState(
            history, started, ended, flow, cognitive);

        Assert.InRange(assessment.SessionDurationMinutes, 4.5, 5.5);
        // Duration below fatigued threshold → not Fatigued (would be if we
        // were using UtcNow and computing 6h+).
        Assert.NotEqual(FlowStateKind.Fatigued, assessment.State);
    }

    // ── FatigueScore is real, not the old 0.0 stub ───────────────────────

    [Fact]
    public void ComputeSessionFlowState_ReturnsNonZeroFatigueWhenSignalsDegrade()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-30);

        // Accuracy collapses + RT doubles → 3-factor model should produce
        // noticeable fatigue.
        var history = BuildHistory(
            started,
            (true, 1200),
            (true, 1300),
            (true, 1400),
            (false, 2800),
            (false, 3200),
            (false, 3500),
            (false, 3800),
            (false, 4100));

        var (_, fatigue) = SessionEndpoints.ComputeSessionFlowState(
            history, started, null, flow, cognitive);

        Assert.True(fatigue > 0.0, $"Expected non-zero fatigue, got {fatigue}");
    }

    // ── Consecutive-correct streak measured from the tail ────────────────

    [Fact]
    public void ComputeSessionFlowState_ConsecutiveCorrect_CountedFromTail()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);

        var history = BuildHistory(
            started,
            (true, 1200),
            (false, 2000),   // breaks any earlier streak
            (true, 1100),
            (true, 1150),
            (true, 1080));

        var (assessment, _) = SessionEndpoints.ComputeSessionFlowState(
            history, started, null, flow, cognitive);

        Assert.Equal(3, assessment.ConsecutiveCorrect);
    }

    [Fact]
    public void ComputeSessionFlowState_TailIncorrect_ZeroStreak()
    {
        var (flow, cognitive) = CreateServices();
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);

        var history = BuildHistory(
            started,
            (true, 1200),
            (true, 1300),
            (false, 2500));   // last is wrong

        var (assessment, _) = SessionEndpoints.ComputeSessionFlowState(
            history, started, null, flow, cognitive);

        Assert.Equal(0, assessment.ConsecutiveCorrect);
    }

    // ── FlowStateEndpoints.ToResponse wire-format regression ─────────────

    [Fact]
    public void ToResponse_EmitsCamelCaseState()
    {
        var assessment = new FlowStateAssessment(
            State: FlowStateKind.InFlow,
            FatigueLevel: 0.2,
            AccuracyTrend: 0.25,
            ConsecutiveCorrect: 4,
            SessionDurationMinutes: 10,
            RecommendedAction: FlowStateAction.Continue,
            CooldownMinutes: null,
            DifficultyAdjustmentAdvice: null);

        var response = FlowStateEndpoints.ToResponse(assessment);

        Assert.Equal("inFlow", response.State);
        Assert.Equal("continue", response.RecommendedAction);
        Assert.Null(response.CooldownMinutes);
        Assert.Null(response.DifficultyAdjustment);
    }

    [Fact]
    public void ToResponse_FatiguedEmitsBreakAndCooldown()
    {
        var assessment = new FlowStateAssessment(
            State: FlowStateKind.Fatigued,
            FatigueLevel: 0.85,
            AccuracyTrend: 0,
            ConsecutiveCorrect: 0,
            SessionDurationMinutes: 50,
            RecommendedAction: FlowStateAction.SuggestBreak,
            CooldownMinutes: 15,
            DifficultyAdjustmentAdvice: DifficultyAdjustment.Ease);

        var response = FlowStateEndpoints.ToResponse(assessment);

        Assert.Equal("fatigued", response.State);
        Assert.Equal("suggest_break", response.RecommendedAction);
        Assert.Equal(15, response.CooldownMinutes);
        Assert.Equal("ease", response.DifficultyAdjustment);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (IFlowStateService Flow, ICognitiveLoadService Cognitive) CreateServices()
    {
        var cognitive = new CognitiveLoadService();
        var flow = new FlowStateService(cognitive, NullLogger<FlowStateService>.Instance);
        return (flow, cognitive);
    }

    /// <summary>
    /// Build a <see cref="SessionAttemptHistoryDocument"/> with the supplied
    /// (isCorrect, responseTimeMs) attempts in order, timestamps spaced 20
    /// seconds apart starting from <paramref name="sessionStartedAt"/>.
    /// </summary>
    private static SessionAttemptHistoryDocument BuildHistory(
        DateTimeOffset sessionStartedAt,
        params (bool IsCorrect, int ResponseTimeMs)[] attempts)
    {
        var doc = new SessionAttemptHistoryDocument
        {
            Id = "session-1",
            SessionId = "session-1",
            StudentId = "student-1",
        };
        for (int i = 0; i < attempts.Length; i++)
        {
            doc.Attempts.Add(new SessionAttemptItem
            {
                AttemptId = $"session-1-{i}",
                QuestionId = $"q-{i}",
                ConceptId = "concept-a",
                QuestionType = "short-answer",
                IsCorrect = attempts[i].IsCorrect,
                ResponseTimeMs = attempts[i].ResponseTimeMs,
                Timestamp = sessionStartedAt.AddSeconds(20 * (i + 1)),
                PriorMastery = 0.4,
                PosteriorMastery = attempts[i].IsCorrect ? 0.5 : 0.35,
            });
        }
        return doc;
    }
}
