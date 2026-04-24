// =============================================================================
// Cena Platform — HintStuckShadowService (RDY-063 Phase 2a)
//
// The production implementation of shadow-mode classification. Builds a
// StuckContext from the live LearningSessionQueueProjection + question
// state, runs HybridStuckClassifier, persists the label. Never mutates
// the hint response (that's the "shadow" contract, enforced by an
// architecture test on the calling endpoint).
//
// Behaviour contract:
//   - If Cena:StuckClassifier:Enabled=false → method returns immediately
//     (Task.CompletedTask). Zero I/O, zero cost.
//   - On any exception → swallowed + metricised. Caller cannot observe
//     classifier health from its return path.
//   - Diagnosis is persisted async; return does NOT wait for Marten save.
// =============================================================================

using System.Diagnostics;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Diagnosis;

public sealed class HintStuckShadowService : IHintStuckShadowService
{
    private readonly IStuckTypeClassifier _classifier;
    private readonly IStuckContextBuilder _contextBuilder;
    private readonly IStuckDiagnosisRepository _repository;
    private readonly IOptionsMonitor<StuckClassifierOptions> _options;
    private readonly StuckClassifierMetrics _metrics;
    private readonly ILogger<HintStuckShadowService> _logger;

    public HintStuckShadowService(
        IStuckTypeClassifier classifier,
        IStuckContextBuilder contextBuilder,
        IStuckDiagnosisRepository repository,
        IOptionsMonitor<StuckClassifierOptions> options,
        StuckClassifierMetrics metrics,
        ILogger<HintStuckShadowService> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordShadowDiagnosisAsync(
        string studentId,
        string sessionId,
        string questionId,
        LearningSessionQueueProjection queue,
        QuestionDocument question,
        int hintLevel,
        string locale,
        CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled) return;

        try
        {
            var sw = Stopwatch.StartNew();
            var context = BuildContext(studentId, sessionId, questionId, queue, question, locale);
            var diagnosis = await _classifier.DiagnoseAsync(context, ct);
            sw.Stop();

            _metrics.RecordLatency(sw.ElapsedMilliseconds > int.MaxValue
                ? int.MaxValue : (int)sw.ElapsedMilliseconds, diagnosis.Source);

            _logger.LogInformation(
                "[STUCK_DIAG] session={SessionId} q={QuestionId} primary={Primary} confidence={Confidence:F2} " +
                "strategy={Strategy} source={Source} latencyMs={Latency} hintLevel={HintLevel} locale={Locale} reason={Reason}",
                sessionId, questionId, diagnosis.Primary, diagnosis.PrimaryConfidence,
                diagnosis.SuggestedStrategy, diagnosis.Source, diagnosis.LatencyMs,
                hintLevel, locale, diagnosis.SourceReasonCode);

            // Only persist actionable diagnoses — persisting Unknown adds
            // storage cost without any analytic value.
            if (diagnosis.Primary != StuckType.Unknown)
            {
                await _repository.PersistAsync(
                    sessionId,
                    context.StudentAnonId,
                    questionId,
                    diagnosis,
                    opts.RetentionDays,
                    ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Request cancelled upstream; drop silently.
        }
        catch (Exception ex)
        {
            _metrics.RecordPersistFailure(ex.GetType().Name);
            _logger.LogWarning(ex,
                "HintStuckShadowService failed for session {SessionId} question {QuestionId} (non-fatal)",
                sessionId, questionId);
        }
    }

    private StuckContext BuildContext(
        string studentId, string sessionId, string questionId,
        LearningSessionQueueProjection queue, QuestionDocument question, string locale)
    {
        var now = DateTimeOffset.UtcNow;

        // ── Attempts on THIS question only (session-scoped). ──────────
        var questionAttempts = queue.AnsweredQuestions
            .Where(a => a.QuestionId == questionId)
            .OrderBy(a => a.AnsweredAt)
            .ToList();

        DateTime prevAt = queue.CurrentQuestionShownAt ?? queue.StartedAt;
        var stuckAttempts = questionAttempts.Select(a =>
        {
            var sincePrev = (int)Math.Max(0, (a.AnsweredAt - prevAt).TotalSeconds);
            prevAt = a.AnsweredAt;
            return new StuckContextAttempt(
                SubmittedAt: new DateTimeOffset(a.AnsweredAt.Ticks, TimeSpan.Zero),
                LatexInputScrubbed: a.SelectedOption,   // MCQ selection; already PII-free
                WasCorrect: a.IsCorrect,
                TimeSincePrevAttemptSec: sincePrev,
                InputChangeRatio: 0f,                   // MCQ doesn't have edit-distance
                ErrorType: null);
        }).ToList();

        // ── Aggregate session signals. ────────────────────────────────
        var timeOnQuestionSec = queue.CurrentQuestionShownAt.HasValue
            ? (int)Math.Max(0, (DateTime.UtcNow - queue.CurrentQuestionShownAt.Value).TotalSeconds)
            : 0;
        var hintsRequestedSoFar = queue.HintsUsedByQuestion.GetValueOrDefault(questionId, 0);
        var recentAccuracy = queue.GetAccuracy();
        // Proxy for items bailed: questions seen in the queue minus answered
        // (approximation; proper bail-tracking is a Phase 2b metric).
        var itemsBailed = Math.Max(0, queue.SeenQuestionIds.Count - queue.AnsweredQuestions.Count - 1);

        // Response-time ratio: session avg vs a default baseline (session
        // doesn't carry per-student baseline yet; default 1.0 = neutral).
        double rtRatio = 1.0;

        // ── Build input + go through builder's PII guard. ─────────────
        var inputs = new StuckContextInputs(
            StudentId: studentId,
            SessionId: sessionId,
            Locale: locale,
            Question: new StuckContextQuestion(
                QuestionId: questionId,
                CanonicalTextByLocaleScrubbed: null,  // prompt text lookup deferred to Phase 2b
                ChapterId: null,                       // advancement integration → Phase 2b
                LearningObjectiveIds: question.LearningObjectiveId is null
                    ? Array.Empty<string>()
                    : new[] { question.LearningObjectiveId },
                QuestionType: null,
                QuestionDifficulty: (float)(question.DifficultyElo / 3000.0)),  // elo → 0..1
            Advancement: new StuckContextAdvancement(
                CurrentChapterId: null,
                CurrentChapterStatus: null,
                CurrentChapterRetention: 0f,
                ChaptersMasteredCount: 0,
                ChaptersTotalCount: 0),
            Attempts: stuckAttempts,
            SessionSignals: new StuckContextSessionSignals(
                TimeOnQuestionSec: timeOnQuestionSec,
                HintsRequestedSoFar: hintsRequestedSoFar,
                ItemsSolvedInSession: queue.CorrectAnswers,
                ItemsBailedInSession: itemsBailed,
                RecentAccuracy: recentAccuracy,
                ResponseTimeRatio: rtRatio),
            AsOf: now);

        return _contextBuilder.Build(inputs);
    }
}
