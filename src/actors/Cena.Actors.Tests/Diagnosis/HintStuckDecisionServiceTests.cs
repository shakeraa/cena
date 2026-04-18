// =============================================================================
// Cena Platform — HintStuckDecisionService tests (RDY-063 Phase 2b)
//
// Covers the three flag modes + failure paths:
//
//   Mode 1: Enabled=false                       → no classifier call, no shadow
//   Mode 2: Enabled=true, Adjustment=false      → shadow only (fire-and-forget)
//   Mode 3: Enabled=true, Adjustment=true       → await classifier, apply adjustment
//
//   Failure paths:
//     - classifier throws → NoChange, not bubbled
//     - classifier times out → NoChange (level unchanged), logs
//     - low-confidence diagnosis → NoChange
// =============================================================================

using Cena.Actors.Diagnosis;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Diagnosis;

public class HintStuckDecisionServiceTests
{
    private static LearningSessionQueueProjection Queue() => new()
    {
        Id = "s", SessionId = "s", StudentId = "stu",
        StartedAt = DateTime.UtcNow.AddMinutes(-5),
        CurrentQuestionId = "q-1",
        CurrentQuestionShownAt = DateTime.UtcNow.AddSeconds(-30),
        TotalQuestionsAttempted = 1,
        HintsUsedByQuestion = new() { ["q-1"] = 0 },
    };

    private static QuestionDocument Question() => new()
    {
        Id = "q-1", QuestionId = "q-1", ConceptId = "c-1",
        DifficultyElo = 1500, LearningObjectiveId = "lo-1",
    };

    private static StuckClassifierOptions Opts(bool enabled, bool adjust) => new()
    {
        Enabled = enabled,
        HintAdjustmentEnabled = adjust,
        HintAdjustmentTimeoutMs = 500,
        HintAdjustmentMinConfidence = 0.65f,
        ClassifierVersion = "v1-test",
        AnonSalt = "salt-v1",
        RetentionDays = 30,
    };

    private static (HintStuckDecisionService svc, RecordingClassifier clf, CountingRepo repo, RecordingShadow shadow)
        Build(StuckClassifierOptions opts, IStuckTypeClassifier? overrideClassifier = null)
    {
        var clf = overrideClassifier as RecordingClassifier ?? new RecordingClassifier();
        var classifier = overrideClassifier ?? clf;
        var repo = new CountingRepo();
        var shadow = new RecordingShadow();
        var svc = new HintStuckDecisionService(
            classifier,
            new StuckContextBuilder(new StuckAnonymizer(opts.AnonSalt)),
            repo,
            new DiagnosisHintLevelAdjuster(),
            shadow,
            new StaticOpts(opts),
            new StuckClassifierMetrics(new DummyMeterFactory()),
            NullLogger<HintStuckDecisionService>.Instance);
        return (svc, clf, repo, shadow);
    }

    [Fact]
    public async Task Mode1_FlagOff_NoClassifierCall_NoShadow_LevelUnchanged()
    {
        var (svc, clf, repo, shadow) = Build(Opts(enabled: false, adjust: false));

        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 2, maxLevel: 3, "en");

        Assert.Equal(2, outcome.AdjustedLevel);
        Assert.False(outcome.Adjusted);
        Assert.Equal("decision.classifier_off", outcome.ReasonCode);
        Assert.False(clf.Called);
        Assert.Equal(0, shadow.CallCount);
        Assert.Equal(0, repo.PersistCount);
    }

    [Fact]
    public async Task Mode2_ShadowOnly_DelegatesToShadow_LevelUnchanged()
    {
        var (svc, clf, repo, shadow) = Build(Opts(enabled: true, adjust: false));

        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 2, maxLevel: 3, "en");

        Assert.Equal(2, outcome.AdjustedLevel);
        Assert.False(outcome.Adjusted);
        Assert.Equal("decision.shadow_only", outcome.ReasonCode);
        // Shadow service got the call (fire-and-forget from decision service).
        Assert.Equal(1, shadow.CallCount);
        // Decision service did NOT call the classifier directly — shadow owns that.
        Assert.False(clf.Called);
    }

    [Fact]
    public async Task Mode3_AdjustmentOn_StrongDiagnosis_ClampsLevel()
    {
        var diag = new StuckDiagnosis(
            Primary: StuckType.MetaStuck, PrimaryConfidence: 0.8f,
            Secondary: StuckType.Encoding, SecondaryConfidence: 0.4f,
            SuggestedStrategy: StuckScaffoldStrategy.Regroup,
            FocusChapterId: null, ShouldInvolveTeacher: true,
            Source: StuckDiagnosisSource.Heuristic,
            ClassifierVersion: "v1-test",
            DiagnosedAt: DateTimeOffset.UtcNow, LatencyMs: 10,
            SourceReasonCode: "test");

        var clf = new RecordingClassifier(diag);
        var (svc, _, repo, shadow) = Build(Opts(enabled: true, adjust: true), clf);

        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 3, maxLevel: 3, "en");

        Assert.Equal(1, outcome.AdjustedLevel);  // MetaStuck → 1
        Assert.True(outcome.Adjusted);
        Assert.Equal(StuckType.MetaStuck, outcome.Primary);
        Assert.True(clf.Called);
        // Persistence is fire-and-forget via Task.Run — shadow service
        // is NOT called in Mode 3; decision service persists directly.
        Assert.Equal(0, shadow.CallCount);
    }

    [Fact]
    public async Task Mode3_LowConfidence_NoAdjustment()
    {
        var diag = new StuckDiagnosis(
            Primary: StuckType.MetaStuck, PrimaryConfidence: 0.4f,  // below 0.65
            Secondary: StuckType.Unknown, SecondaryConfidence: 0f,
            SuggestedStrategy: StuckScaffoldStrategy.Regroup,
            FocusChapterId: null, ShouldInvolveTeacher: false,
            Source: StuckDiagnosisSource.Heuristic,
            ClassifierVersion: "v1-test",
            DiagnosedAt: DateTimeOffset.UtcNow, LatencyMs: 5,
            SourceReasonCode: "test");

        var clf = new RecordingClassifier(diag);
        var (svc, _, _, _) = Build(Opts(enabled: true, adjust: true), clf);

        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 3, maxLevel: 3, "en");

        Assert.Equal(3, outcome.AdjustedLevel);  // kept
        Assert.False(outcome.Adjusted);
    }

    [Fact]
    public async Task Mode3_ClassifierThrows_ReturnsOriginalLevel_NeverBubbles()
    {
        var clf = new ThrowingClassifier();
        var (svc, _, _, _) = Build(Opts(enabled: true, adjust: true), clf);

        // Must NOT throw — contract.
        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 2, maxLevel: 3, "en");

        Assert.Equal(2, outcome.AdjustedLevel);
        Assert.False(outcome.Adjusted);
        Assert.Equal("decision.exception", outcome.ReasonCode);
    }

    [Fact]
    public async Task Mode3_ClassifierTimesOut_ReturnsOriginalLevel()
    {
        var slow = new SlowClassifier(TimeSpan.FromSeconds(5));
        var opts = Opts(enabled: true, adjust: true);
        opts.HintAdjustmentTimeoutMs = 100;  // tight timeout
        var (svc, _, _, _) = Build(opts, slow);

        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 2, maxLevel: 3, "en");

        Assert.Equal(2, outcome.AdjustedLevel);
        Assert.False(outcome.Adjusted);
        Assert.Equal("decision.timeout", outcome.ReasonCode);
    }

    [Fact]
    public async Task Mode3_ExternalCancellation_PropagatesToClassifier()
    {
        var slow = new SlowClassifier(TimeSpan.FromSeconds(5));
        var (svc, _, _, _) = Build(Opts(enabled: true, adjust: true), slow);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var outcome = await svc.DecideAsync(
            "stu", "sess", "q-1", Queue(), Question(),
            requestedLevel: 2, maxLevel: 3, "en", cts.Token);

        // Either timeout or caller cancellation — both produce NoChange,
        // but we log the timeout reason because our internal tight
        // timeout also fires. The point is: hint response never blocks.
        Assert.Equal(2, outcome.AdjustedLevel);
        Assert.False(outcome.Adjusted);
    }

    // ── Test doubles ───────────────────────────────────────────────────

    private sealed class RecordingClassifier : IStuckTypeClassifier
    {
        public bool Called { get; private set; }
        private readonly StuckDiagnosis _result;
        public RecordingClassifier(StuckDiagnosis? r = null)
        {
            _result = r ?? StuckDiagnosis.Unknown("v1", StuckDiagnosisSource.None, 0);
        }
        public Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingClassifier : IStuckTypeClassifier
    {
        public Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class SlowClassifier : IStuckTypeClassifier
    {
        private readonly TimeSpan _delay;
        public SlowClassifier(TimeSpan d) { _delay = d; }
        public async Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct);
            return StuckDiagnosis.Unknown("v1", StuckDiagnosisSource.None, 0);
        }
    }

    private sealed class StaticOpts : IOptionsMonitor<StuckClassifierOptions>
    {
        public StaticOpts(StuckClassifierOptions v) { CurrentValue = v; }
        public StuckClassifierOptions CurrentValue { get; }
        public StuckClassifierOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<StuckClassifierOptions, string?> _) => null;
    }

    private sealed class CountingRepo : IStuckDiagnosisRepository
    {
        public int PersistCount { get; private set; }
        public Task PersistAsync(string s, string a, string q, StuckDiagnosis d, int r, CancellationToken c = default)
        { PersistCount++; return Task.CompletedTask; }
        public Task<IReadOnlyList<StuckDiagnosisDocument>> GetRecentByQuestionAsync(string q, int l, CancellationToken c = default)
            => Task.FromResult<IReadOnlyList<StuckDiagnosisDocument>>(Array.Empty<StuckDiagnosisDocument>());
        public Task<IReadOnlyList<StuckItemAggregate>> GetTopItemsAsync(StuckType? f, int d, int l, CancellationToken c = default)
            => Task.FromResult<IReadOnlyList<StuckItemAggregate>>(Array.Empty<StuckItemAggregate>());
        public Task<IReadOnlyDictionary<StuckType, int>> GetDistributionAsync(int d, CancellationToken c = default)
            => Task.FromResult<IReadOnlyDictionary<StuckType, int>>(new Dictionary<StuckType, int>());
    }

    private sealed class RecordingShadow : IHintStuckShadowService
    {
        public int CallCount { get; private set; }
        public Task RecordShadowDiagnosisAsync(string s, string sess, string q,
            LearningSessionQueueProjection queue, QuestionDocument question, int lvl, string loc, CancellationToken ct = default)
        { CallCount++; return Task.CompletedTask; }
    }

    private sealed class DummyMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
