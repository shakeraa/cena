// =============================================================================
// Cena Platform — HeuristicStuckClassifier tests (RDY-063)
//
// Golden fixtures covering each rule branch. These tests are the
// confidence-band contract: if a rule's confidence is adjusted, the
// corresponding test must be adjusted explicitly (no hidden drift).
// =============================================================================

using Cena.Actors.Diagnosis;

namespace Cena.Actors.Tests.Diagnosis;

public class HeuristicStuckClassifierTests
{
    private static readonly StuckClassifierOptions Options = new()
    {
        Enabled = true,
        ClassifierVersion = "v1.0.0-test",
    };

    private static StuckContext BuildCtx(
        int attemptCount = 0,
        int timeOnQuestionSec = 30,
        int itemsBailed = 0,
        int itemsSolved = 0,
        double recentAccuracy = 0.5,
        float retention = 0.8f,
        string? chapterStatus = "InProgress",
        Func<int, StuckContextAttempt>? attemptFactory = null)
    {
        attemptFactory ??= i => new StuckContextAttempt(
            DateTimeOffset.UtcNow.AddSeconds(-10),
            LatexInputScrubbed: $"x = {i}",
            WasCorrect: false,
            TimeSincePrevAttemptSec: 5,
            InputChangeRatio: 0.2f,
            ErrorType: null);

        var attempts = Enumerable.Range(0, attemptCount).Select(attemptFactory).ToList();
        return new StuckContext(
            SessionId: "sess-1",
            StudentAnonId: "anon-abc",
            Question: new StuckContextQuestion(
                QuestionId: "q-1",
                CanonicalTextByLocaleScrubbed: "solve for x",
                ChapterId: "ch-1",
                LearningObjectiveIds: new[] { "lo-1" },
                QuestionType: "free_response",
                QuestionDifficulty: 0.5f),
            Advancement: new StuckContextAdvancement(
                CurrentChapterId: "ch-1",
                CurrentChapterStatus: chapterStatus,
                CurrentChapterRetention: retention,
                ChaptersMasteredCount: 2,
                ChaptersTotalCount: 10),
            Attempts: attempts,
            SessionSignals: new StuckContextSessionSignals(
                TimeOnQuestionSec: timeOnQuestionSec,
                HintsRequestedSoFar: 0,
                ItemsSolvedInSession: itemsSolved,
                ItemsBailedInSession: itemsBailed,
                RecentAccuracy: recentAccuracy,
                ResponseTimeRatio: 1.0),
            Locale: "en",
            AsOf: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task R1_BailsAndLowAccuracy_ClassifiedAsMotivational()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(itemsBailed: 3, recentAccuracy: 0.1);

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Motivational, result.Primary);
        Assert.Equal(StuckScaffoldStrategy.Encouragement, result.SuggestedStrategy);
        Assert.True(result.PrimaryConfidence >= 0.7f);
        Assert.Equal(StuckDiagnosisSource.Heuristic, result.Source);
    }

    [Fact]
    public async Task R2_ZeroAttemptsLongTime_ClassifiedAsEncoding()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(attemptCount: 0, timeOnQuestionSec: 150);

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Encoding, result.Primary);
        Assert.Equal(StuckScaffoldStrategy.Rephrase, result.SuggestedStrategy);
        Assert.False(result.ShouldInvolveTeacher);
    }

    [Fact]
    public async Task R2_LongTimeAfterSolvedStreak_LeansMotivational()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(attemptCount: 0, timeOnQuestionSec: 200, itemsSolved: 7);

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Motivational, result.Primary);
    }

    [Fact]
    public async Task R3_RepeatedSameFirstToken_ClassifiedAsMisconception()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(
            attemptCount: 4,
            attemptFactory: i => new StuckContextAttempt(
                DateTimeOffset.UtcNow, "x = 5 + something-else", false, 5, 0.1f, null));

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Misconception, result.Primary);
        Assert.Equal(StuckScaffoldStrategy.ContradictionPrompt, result.SuggestedStrategy);
    }

    [Fact]
    public async Task R3b_HighVarianceAttempts_ClassifiedAsStrategic()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(
            attemptCount: 3,
            attemptFactory: i => new StuckContextAttempt(
                DateTimeOffset.UtcNow,
                i == 0 ? "x=2" : i == 1 ? "use Pythagoras" : "\\int ...",
                false, 10, 0.9f, null));

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Strategic, result.Primary);
        Assert.Equal(StuckScaffoldStrategy.DecompositionPrompt, result.SuggestedStrategy);
    }

    [Fact]
    public async Task R4_LateStepError_ClassifiedAsProcedural()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(
            attemptCount: 1,
            attemptFactory: _ => new StuckContextAttempt(
                DateTimeOffset.UtcNow, "x = 5.3", false, 20, 0.2f, "Arithmetic"));

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Procedural, result.Primary);
        Assert.Equal(StuckScaffoldStrategy.ShowNextStep, result.SuggestedStrategy);
    }

    [Fact]
    public async Task R5_LowRetentionNoAttempts_ClassifiedAsMetaStuck_LeansTeacher()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(attemptCount: 0, timeOnQuestionSec: 30, retention: 0.15f);

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.MetaStuck, result.Primary);
        Assert.True(result.ShouldInvolveTeacher);
    }

    [Fact]
    public async Task R6_BlankAfterPause_ClassifiedAsRecall()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = BuildCtx(
            attemptCount: 1,
            attemptFactory: _ => new StuckContextAttempt(
                DateTimeOffset.UtcNow, "", false, 45, 0f, null));

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Recall, result.Primary);
        Assert.Equal(StuckScaffoldStrategy.ShowDefinition, result.SuggestedStrategy);
    }

    [Fact]
    public async Task NoRuleFired_ReturnsUnknownHeuristic()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        // One good attempt, normal timing — no heuristic rule should match.
        var ctx = BuildCtx(
            attemptCount: 1,
            attemptFactory: _ => new StuckContextAttempt(
                DateTimeOffset.UtcNow, "x = 7 + y", false, 15, 0.3f, null));

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.Equal(StuckType.Unknown, result.Primary);
        Assert.Equal(StuckDiagnosisSource.Heuristic, result.Source);
        Assert.Equal("heuristic.no_rule_fired", result.SourceReasonCode);
    }

    [Fact]
    public async Task EmptyInput_NeverThrows_ReturnsUnknown()
    {
        var classifier = new HeuristicStuckClassifier(Options);
        var ctx = new StuckContext(
            SessionId: "s",
            StudentAnonId: "a",
            Question: new StuckContextQuestion("q", null, null, Array.Empty<string>(), null, null),
            Advancement: new StuckContextAdvancement(null, null, 0, 0, 0),
            Attempts: Array.Empty<StuckContextAttempt>(),
            SessionSignals: new StuckContextSessionSignals(0, 0, 0, 0, 0, 0),
            Locale: "en",
            AsOf: DateTimeOffset.UtcNow);

        var result = await classifier.DiagnoseAsync(ctx);

        Assert.NotNull(result);
        Assert.Equal(StuckType.Unknown, result.Primary);
    }
}
