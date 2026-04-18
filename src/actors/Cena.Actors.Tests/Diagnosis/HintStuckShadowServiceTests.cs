// =============================================================================
// Cena Platform — HintStuckShadowService tests (RDY-063 Phase 2a)
//
// Three behaviour contracts:
//   1. Flag off → RecordShadowDiagnosisAsync is a no-op (no classifier
//      call, no persist attempt). Proved by injecting a ThrowingClassifier
//      that would fail if invoked.
//   2. Flag on + classifier fires → repository.Persist is called ONLY
//      if diagnosis.Primary != Unknown (we don't pollute storage with
//      low-signal Unknown entries).
//   3. Flag on + classifier throws → service catches + metricises;
//      caller does NOT observe the exception.
// =============================================================================

using Cena.Actors.Diagnosis;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Diagnosis;

public class HintStuckShadowServiceTests
{
    private static readonly StuckClassifierOptions EnabledOpts = new()
    {
        Enabled = true,
        ClassifierVersion = "v1.0.0-test",
        RetentionDays = 30,
        AnonSalt = "test-salt",
    };
    private static readonly StuckClassifierOptions DisabledOpts = new()
    {
        Enabled = false,
        ClassifierVersion = "v1.0.0-test",
        AnonSalt = "test-salt",
    };

    private static LearningSessionQueueProjection MakeQueue() => new()
    {
        Id = "sess-1",
        SessionId = "sess-1",
        StudentId = "stu-1",
        StartedAt = DateTime.UtcNow.AddMinutes(-5),
        CurrentQuestionId = "q-1",
        CurrentQuestionShownAt = DateTime.UtcNow.AddSeconds(-30),
        TotalQuestionsAttempted = 3,
        CorrectAnswers = 2,
        AnsweredQuestions = new List<QuestionHistory>
        {
            new() { QuestionId = "q-1", AnsweredAt = DateTime.UtcNow.AddSeconds(-20),
                    IsCorrect = false, TimeSpentSeconds = 10, SelectedOption = "A" }
        },
        HintsUsedByQuestion = new Dictionary<string, int> { ["q-1"] = 0 },
    };

    private static QuestionDocument MakeQuestion() => new()
    {
        Id = "q-1",
        QuestionId = "q-1",
        ConceptId = "c-1",
        Subject = "math",
        DifficultyElo = 1500,
        LearningObjectiveId = "lo-1",
    };

    [Fact]
    public async Task FlagOff_DoesNotCallClassifier_OrRepository()
    {
        var throwing = new ThrowingClassifier();
        var repo = new CountingRepository();
        var svc = new HintStuckShadowService(
            throwing,
            new StuckContextBuilder(new StuckAnonymizer("salt")),
            repo,
            new StaticOptionsMonitor(DisabledOpts),
            new StuckClassifierMetrics(new DummyMeterFactory()),
            NullLogger<HintStuckShadowService>.Instance);

        await svc.RecordShadowDiagnosisAsync(
            "stu-1", "sess-1", "q-1", MakeQueue(), MakeQuestion(),
            hintLevel: 1, locale: "en");

        Assert.False(throwing.Called, "Classifier must not be called when flag is off");
        Assert.Equal(0, repo.PersistCount);
    }

    [Fact]
    public async Task FlagOn_ActionableDiagnosis_Persisted()
    {
        var fixedDiag = new StuckDiagnosis(
            Primary: StuckType.Procedural, PrimaryConfidence: 0.8f,
            Secondary: StuckType.Recall, SecondaryConfidence: 0.3f,
            SuggestedStrategy: StuckScaffoldStrategy.ShowNextStep,
            FocusChapterId: null, ShouldInvolveTeacher: false,
            Source: StuckDiagnosisSource.Heuristic,
            ClassifierVersion: "v1.0.0-test",
            DiagnosedAt: DateTimeOffset.UtcNow, LatencyMs: 5,
            SourceReasonCode: "test");

        var classifier = new FixedClassifier(fixedDiag);
        var repo = new CountingRepository();
        var svc = new HintStuckShadowService(
            classifier,
            new StuckContextBuilder(new StuckAnonymizer("salt")),
            repo,
            new StaticOptionsMonitor(EnabledOpts),
            new StuckClassifierMetrics(new DummyMeterFactory()),
            NullLogger<HintStuckShadowService>.Instance);

        await svc.RecordShadowDiagnosisAsync(
            "stu-1", "sess-1", "q-1", MakeQueue(), MakeQuestion(),
            hintLevel: 1, locale: "en");

        Assert.True(classifier.Called);
        Assert.Equal(1, repo.PersistCount);
        Assert.Equal(StuckType.Procedural, repo.LastDiagnosis?.Primary);
        Assert.Equal("sess-1", repo.LastSessionId);
        Assert.NotEqual("stu-1", repo.LastAnonId);  // never the raw id
    }

    [Fact]
    public async Task FlagOn_UnknownDiagnosis_NotPersisted()
    {
        var unknown = StuckDiagnosis.Unknown("v1.0.0-test", StuckDiagnosisSource.None, 0);
        var classifier = new FixedClassifier(unknown);
        var repo = new CountingRepository();
        var svc = new HintStuckShadowService(
            classifier,
            new StuckContextBuilder(new StuckAnonymizer("salt")),
            repo,
            new StaticOptionsMonitor(EnabledOpts),
            new StuckClassifierMetrics(new DummyMeterFactory()),
            NullLogger<HintStuckShadowService>.Instance);

        await svc.RecordShadowDiagnosisAsync(
            "stu-1", "sess-1", "q-1", MakeQueue(), MakeQuestion(),
            hintLevel: 1, locale: "en");

        Assert.True(classifier.Called);
        Assert.Equal(0, repo.PersistCount);
    }

    [Fact]
    public async Task ClassifierThrows_SwallowedSilently()
    {
        var throwing = new ThrowingClassifier();
        var repo = new CountingRepository();
        var svc = new HintStuckShadowService(
            throwing,
            new StuckContextBuilder(new StuckAnonymizer("salt")),
            repo,
            new StaticOptionsMonitor(EnabledOpts),
            new StuckClassifierMetrics(new DummyMeterFactory()),
            NullLogger<HintStuckShadowService>.Instance);

        // Must not throw — shadow-mode contract.
        await svc.RecordShadowDiagnosisAsync(
            "stu-1", "sess-1", "q-1", MakeQueue(), MakeQuestion(),
            hintLevel: 1, locale: "en");

        Assert.True(throwing.Called);
        Assert.Equal(0, repo.PersistCount);
    }

    // ── Test doubles ───────────────────────────────────────────────────

    private sealed class StaticOptionsMonitor : IOptionsMonitor<StuckClassifierOptions>
    {
        public StaticOptionsMonitor(StuckClassifierOptions v) { CurrentValue = v; }
        public StuckClassifierOptions CurrentValue { get; }
        public StuckClassifierOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<StuckClassifierOptions, string?> _) => null;
    }

    private sealed class ThrowingClassifier : IStuckTypeClassifier
    {
        public bool Called { get; private set; }
        public Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class FixedClassifier : IStuckTypeClassifier
    {
        private readonly StuckDiagnosis _d;
        public bool Called { get; private set; }
        public FixedClassifier(StuckDiagnosis d) { _d = d; }
        public Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
        { Called = true; return Task.FromResult(_d); }
    }

    private sealed class CountingRepository : IStuckDiagnosisRepository
    {
        public int PersistCount { get; private set; }
        public StuckDiagnosis? LastDiagnosis { get; private set; }
        public string? LastSessionId { get; private set; }
        public string? LastAnonId { get; private set; }

        public Task PersistAsync(string sessionId, string studentAnonId,
            string questionId, StuckDiagnosis diagnosis, int retentionDays, CancellationToken ct = default)
        {
            PersistCount++;
            LastDiagnosis = diagnosis;
            LastSessionId = sessionId;
            LastAnonId = studentAnonId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StuckDiagnosisDocument>> GetRecentByQuestionAsync(
            string questionId, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StuckDiagnosisDocument>>(Array.Empty<StuckDiagnosisDocument>());
    }

    private sealed class DummyMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
