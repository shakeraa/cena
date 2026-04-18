// =============================================================================
// Cena Platform — HintStuckDecisionService (RDY-063 Phase 2b)
//
// Three flag modes:
//
//   1. Cena:StuckClassifier:Enabled = false
//      → Return requestedLevel immediately. Zero I/O.
//
//   2. Enabled = true, HintAdjustmentEnabled = false  (Phase 2a — shadow)
//      → Fire-and-forget classifier + persistence.
//      → Return requestedLevel. Zero observable impact on hint response.
//
//   3. Enabled = true, HintAdjustmentEnabled = true   (Phase 2b — active)
//      → Await classifier with bounded timeout.
//      → Pass diagnosis to IHintLevelAdjuster.
//      → Fire-and-forget persistence.
//      → Return adjusted level + diagnosis.
//
// The decision service is the ONLY endpoint-facing classifier surface
// once Phase 2b lands. The underlying shadow service (persist-only)
// stays registered for reuse by non-hint paths.
// =============================================================================

using System.Diagnostics;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Diagnosis;

public sealed class HintStuckDecisionService : IHintStuckDecisionService
{
    private readonly IStuckTypeClassifier _classifier;
    private readonly IStuckContextBuilder _contextBuilder;
    private readonly IStuckDiagnosisRepository _repository;
    private readonly IHintLevelAdjuster _adjuster;
    private readonly IHintStuckShadowService _shadow;
    private readonly IOptionsMonitor<StuckClassifierOptions> _options;
    private readonly StuckClassifierMetrics _metrics;
    private readonly ILogger<HintStuckDecisionService> _logger;

    public HintStuckDecisionService(
        IStuckTypeClassifier classifier,
        IStuckContextBuilder contextBuilder,
        IStuckDiagnosisRepository repository,
        IHintLevelAdjuster adjuster,
        IHintStuckShadowService shadow,
        IOptionsMonitor<StuckClassifierOptions> options,
        StuckClassifierMetrics metrics,
        ILogger<HintStuckDecisionService> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _adjuster = adjuster ?? throw new ArgumentNullException(nameof(adjuster));
        _shadow = shadow ?? throw new ArgumentNullException(nameof(shadow));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HintDecisionOutcome> DecideAsync(
        string studentId,
        string sessionId,
        string questionId,
        LearningSessionQueueProjection queue,
        QuestionDocument question,
        int requestedLevel,
        int maxLevel,
        string locale,
        CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var normalisedReq = Math.Clamp(requestedLevel, 1, Math.Max(1, maxLevel));

        // Mode 1: classifier globally disabled.
        if (!opts.Enabled)
        {
            return NoChange(normalisedReq, "decision.classifier_off", latency: 0);
        }

        // Mode 2: classifier on but adjustment off → Phase 2a shadow path.
        if (!opts.HintAdjustmentEnabled)
        {
            // Fire-and-forget: delegate to the existing shadow service so
            // we have one canonical persist/log path.
            _ = _shadow.RecordShadowDiagnosisAsync(
                studentId, sessionId, questionId, queue, question,
                normalisedReq, locale, ct);
            return NoChange(normalisedReq, "decision.shadow_only", latency: 0);
        }

        // Mode 3: adjustment on → classify synchronously with bounded timeout.
        var sw = Stopwatch.StartNew();
        StuckDiagnosis? diagnosis = null;
        try
        {
            var context = BuildContext(studentId, sessionId, questionId, queue, question, locale);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(50, opts.HintAdjustmentTimeoutMs)));

            try
            {
                diagnosis = await _classifier.DiagnoseAsync(context, cts.Token);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                _metrics.RecordLatency((int)sw.ElapsedMilliseconds, StuckDiagnosisSource.LlmError);
                _logger.LogInformation(
                    "[STUCK_DIAG_TIMEOUT] session={SessionId} q={QuestionId} timeoutMs={Timeout} elapsedMs={Elapsed}",
                    sessionId, questionId, opts.HintAdjustmentTimeoutMs, sw.ElapsedMilliseconds);
                return NoChange(normalisedReq, "decision.timeout", (int)sw.ElapsedMilliseconds);
            }

            sw.Stop();

            // Compute adjustment (rules are synchronous + deterministic).
            var adjustment = _adjuster.Adjust(
                normalisedReq, maxLevel, diagnosis, opts.HintAdjustmentMinConfidence);

            // Persist in background — mirrors the shadow-service contract.
            // We use the diagnosis we already have; NO second classifier call.
            var persistContext = context;
            var persistDiagnosis = diagnosis;
            var retentionDays = opts.RetentionDays;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (persistDiagnosis.Primary != StuckType.Unknown)
                    {
                        await _repository.PersistAsync(
                            sessionId, persistContext.StudentAnonId, questionId,
                            persistDiagnosis, retentionDays, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _metrics.RecordPersistFailure(ex.GetType().Name);
                    _logger.LogDebug(ex, "Decision-path persist failure (non-fatal)");
                }
            });

            _metrics.RecordLatency((int)sw.ElapsedMilliseconds, diagnosis.Source);
            _metrics.RecordAdjustment(adjustment.Changed, diagnosis.Primary);

            _logger.LogInformation(
                "[STUCK_DIAG] session={SessionId} q={QuestionId} primary={Primary} confidence={Confidence:F2} " +
                "strategy={Strategy} source={Source} reqLevel={ReqLevel} adjustedLevel={AdjLevel} " +
                "changed={Changed} reason={Reason} latencyMs={Latency} locale={Locale}",
                sessionId, questionId, diagnosis.Primary, diagnosis.PrimaryConfidence,
                diagnosis.SuggestedStrategy, diagnosis.Source,
                adjustment.OriginalLevel, adjustment.AdjustedLevel,
                adjustment.Changed, adjustment.ReasonCode, sw.ElapsedMilliseconds, locale);

            return new HintDecisionOutcome(
                AdjustedLevel: adjustment.AdjustedLevel,
                Adjusted: adjustment.Changed,
                ReasonCode: adjustment.ReasonCode,
                Primary: diagnosis.Primary,
                PrimaryConfidence: diagnosis.PrimaryConfidence,
                LatencyMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "HintStuckDecisionService failed for session {SessionId} question {QuestionId} (non-fatal)",
                sessionId, questionId);
            _metrics.RecordPersistFailure(ex.GetType().Name);
            return NoChange(normalisedReq, "decision.exception", (int)sw.ElapsedMilliseconds);
        }
    }

    private static HintDecisionOutcome NoChange(int level, string reason, int latency) =>
        new(level, Adjusted: false, reason, Primary: null, PrimaryConfidence: null, LatencyMs: latency);

    private StuckContext BuildContext(
        string studentId, string sessionId, string questionId,
        LearningSessionQueueProjection queue, QuestionDocument question, string locale)
    {
        var now = DateTimeOffset.UtcNow;

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
                LatexInputScrubbed: a.SelectedOption,
                WasCorrect: a.IsCorrect,
                TimeSincePrevAttemptSec: sincePrev,
                InputChangeRatio: 0f,
                ErrorType: null);
        }).ToList();

        var timeOnQuestionSec = queue.CurrentQuestionShownAt.HasValue
            ? (int)Math.Max(0, (DateTime.UtcNow - queue.CurrentQuestionShownAt.Value).TotalSeconds)
            : 0;
        var hintsRequestedSoFar = queue.HintsUsedByQuestion.GetValueOrDefault(questionId, 0);
        var itemsBailed = Math.Max(0, queue.SeenQuestionIds.Count - queue.AnsweredQuestions.Count - 1);

        var inputs = new StuckContextInputs(
            StudentId: studentId,
            SessionId: sessionId,
            Locale: locale,
            Question: new StuckContextQuestion(
                QuestionId: questionId,
                CanonicalTextByLocaleScrubbed: null,
                ChapterId: null,
                LearningObjectiveIds: question.LearningObjectiveId is null
                    ? Array.Empty<string>()
                    : new[] { question.LearningObjectiveId },
                QuestionType: null,
                QuestionDifficulty: (float)(question.DifficultyElo / 3000.0)),
            Advancement: new StuckContextAdvancement(null, null, 0f, 0, 0),
            Attempts: stuckAttempts,
            SessionSignals: new StuckContextSessionSignals(
                TimeOnQuestionSec: timeOnQuestionSec,
                HintsRequestedSoFar: hintsRequestedSoFar,
                ItemsSolvedInSession: queue.CorrectAnswers,
                ItemsBailedInSession: itemsBailed,
                RecentAccuracy: queue.GetAccuracy(),
                ResponseTimeRatio: 1.0),
            AsOf: now);

        return _contextBuilder.Build(inputs);
    }
}
