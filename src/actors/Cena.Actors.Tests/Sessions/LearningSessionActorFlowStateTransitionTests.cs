// =============================================================================
// Cena Platform — RDY-034 slice 3 tests
//
// Verifies LearningSessionActor.EmitFlowStateIfTransitioned:
//   • tracks the trailing consecutive-correct streak correctly
//   • calls IFlowStateService.Assess with the current session signals
//   • emits [FLOW_STATE_TRANSITION] only on state change (idempotent)
//   • populates a meaningful trigger per transition kind
//
// Uses the real FlowStateService + real CognitiveLoadService so the state
// machine + fatigue formula are exercised end-to-end. The actor is driven
// through reflection (its _startedAt/_baseline/_recentAccuracies/_fatigueScore
// fields are internal state populated during HandleEvaluateAnswer) to
// avoid spinning up a full Proto.Actor kernel.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Reflection;
using Cena.Actors.Hints;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Cena.Actors.Services;
using Cena.Actors.Sessions;
using Cena.Actors.Tutoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Sessions;

public sealed class LearningSessionActorFlowStateTransitionTests
{
    // Use a dedicated list-logger so we can assert on emitted entries.
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId _, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((level, formatter(state, ex)));
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static (LearningSessionActor Actor, ListLogger<LearningSessionActor> Logger)
        Build(DateTimeOffset? startedAt = null, double baselineAccuracy = 0.5)
    {
        var bkt = Substitute.For<IBktService>();
        var hintAdjustedBkt = Substitute.For<IHintAdjustedBktService>();
        var cognitiveLoad = new CognitiveLoadService();
        var flowState = new FlowStateService(
            cognitiveLoad, NullLogger<FlowStateService>.Instance);
        var logger = new ListLogger<LearningSessionActor>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));

        var actor = new LearningSessionActor(
            bkt,
            hintAdjustedBkt,
            cognitiveLoad,
            flowState,
            new HintGenerator(),
            new HintGenerationService(),
            new ConfusionDetector(),
            new DisengagementClassifier(),
            Substitute.For<IDeliveryGate>(),
            Substitute.For<IPersonalizedExplanationService>(),
            () => throw new InvalidOperationException("TutorActor not expected"),
            Substitute.For<IConceptGraphCache>(),
            logger,
            meterFactory);

        SetField(actor, "_sessionId", "session-1");
        SetField(actor, "_studentId", "student-1");
        SetField(actor, "_startedAt", startedAt ?? DateTimeOffset.UtcNow.AddMinutes(-5));
        SetField(actor, "_baselineAccuracy", baselineAccuracy);
        SetField(actor, "_baselineResponseTimeMs", 3000.0);

        return (actor, logger);
    }

    private static void SetField(object target, string name, object? value) =>
        typeof(LearningSessionActor)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static T GetField<T>(object target, string name) =>
        (T)typeof(LearningSessionActor)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    // ── Streak tracking ──────────────────────────────────────────────────

    [Fact]
    public void EmitFlowStateIfTransitioned_CorrectAnswers_IncrementsStreak()
    {
        var (actor, _) = Build();

        actor.EmitFlowStateIfTransitioned(true);
        actor.EmitFlowStateIfTransitioned(true);
        actor.EmitFlowStateIfTransitioned(true);

        Assert.Equal(3, GetField<int>(actor, "_consecutiveCorrect"));
    }

    [Fact]
    public void EmitFlowStateIfTransitioned_WrongAnswer_ResetsStreak()
    {
        var (actor, _) = Build();

        actor.EmitFlowStateIfTransitioned(true);
        actor.EmitFlowStateIfTransitioned(true);
        actor.EmitFlowStateIfTransitioned(false);

        Assert.Equal(0, GetField<int>(actor, "_consecutiveCorrect"));
    }

    // ── Transition logging ──────────────────────────────────────────────

    [Fact]
    public void EmitFlowStateIfTransitioned_FirstCall_EmitsTransitionFromInitial()
    {
        var (actor, logger) = Build();
        SetField(actor, "_fatigueScore", 0.1);

        actor.EmitFlowStateIfTransitioned(true);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]")
            && e.Message.Contains("from=initial"));
    }

    [Fact]
    public void EmitFlowStateIfTransitioned_RepeatedSameState_DoesNotLogTwice()
    {
        var (actor, logger) = Build();
        SetField(actor, "_fatigueScore", 0.1);

        actor.EmitFlowStateIfTransitioned(true);   // triggers first transition
        var afterFirst = logger.Entries.Count(e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]"));

        // Second call in the same signals → no new transition expected.
        actor.EmitFlowStateIfTransitioned(true);
        var afterSecond = logger.Entries.Count(e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]"));

        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public void EmitFlowStateIfTransitioned_LongSession_EmitsFatiguedWithTimeoutTrigger()
    {
        var (actor, logger) = Build(startedAt: DateTimeOffset.UtcNow.AddMinutes(-50));
        SetField(actor, "_fatigueScore", 0.1);

        actor.EmitFlowStateIfTransitioned(true);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]")
            && e.Message.Contains("to=Fatigued")
            && e.Message.Contains("trigger=session_timeout"));
    }

    [Fact]
    public void EmitFlowStateIfTransitioned_HighFatigue_EmitsFatiguedWithThresholdTrigger()
    {
        var (actor, logger) = Build();
        SetField(actor, "_fatigueScore", 0.85);   // > 0.7 HighFatigueThreshold

        actor.EmitFlowStateIfTransitioned(false);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]")
            && e.Message.Contains("to=Fatigued")
            && e.Message.Contains("trigger=fatigue_threshold"));
    }

    [Fact]
    public void EmitFlowStateIfTransitioned_LogsFatigueTrendStreakAndDuration()
    {
        var (actor, logger) = Build();
        SetField(actor, "_fatigueScore", 0.15);

        actor.EmitFlowStateIfTransitioned(true);

        var entry = logger.Entries.First(e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]"));
        Assert.Contains("fatigue=0.15", entry.Message);
        Assert.Contains("streak=1", entry.Message);
        Assert.Contains("duration_min=", entry.Message);
    }

    [Fact]
    public void EmitFlowStateIfTransitioned_IncludesSessionAndStudentIds()
    {
        var (actor, logger) = Build();
        SetField(actor, "_fatigueScore", 0.1);

        actor.EmitFlowStateIfTransitioned(true);

        var entry = logger.Entries.First(e =>
            e.Message.Contains("[FLOW_STATE_TRANSITION]"));
        Assert.Contains("session=session-1", entry.Message);
        Assert.Contains("student=student-1", entry.Message);
    }

    // ── Constructor guard ───────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFlowState_Throws()
    {
        var bkt = Substitute.For<IBktService>();
        var hintAdjustedBkt = Substitute.For<IHintAdjustedBktService>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));

        Assert.Throws<ArgumentNullException>(() => new LearningSessionActor(
            bkt,
            hintAdjustedBkt,
            new CognitiveLoadService(),
            flowState: null!,
            new HintGenerator(),
            new HintGenerationService(),
            new ConfusionDetector(),
            new DisengagementClassifier(),
            Substitute.For<IDeliveryGate>(),
            Substitute.For<IPersonalizedExplanationService>(),
            () => throw new InvalidOperationException("unused"),
            Substitute.For<IConceptGraphCache>(),
            NullLogger<LearningSessionActor>.Instance,
            meterFactory));
    }
}
