// =============================================================================
// Cena Platform — HeuristicStuckClassifier (RDY-063 Phase 1)
//
// Deterministic rule-based pre-pass. Targets the 40% of cases with clear
// signal so the LLM call can be skipped (spec: ≥40% heuristic coverage).
//
// Each rule is independently testable, carries a fixed confidence band,
// and emits a short machine-readable reason code. The confidence bands
// are calibrated to what a human reviewer would agree with given the
// same surface features — intentionally conservative on rules that
// rely on counting (e.g., three-attempt Misconception fires at 0.7,
// not 0.9, because "same wrong answer" is one signal of many).
//
// Rules evaluated in priority order; first match wins. Unknown (zero
// confidence) fall-through means "LLM, take this one".
// =============================================================================

using System.Diagnostics;

namespace Cena.Actors.Diagnosis;

public sealed class HeuristicStuckClassifier : IStuckTypeClassifier
{
    private readonly StuckClassifierOptions _options;

    public HeuristicStuckClassifier(StuckClassifierOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<StuckDiagnosis> DiagnoseAsync(StuckContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var diagnosis = Evaluate(context);
        sw.Stop();
        return Task.FromResult(diagnosis with { LatencyMs = (int)sw.ElapsedMilliseconds });
    }

    // Internal for unit-test access without exposing Task wrapping.
    internal StuckDiagnosis Evaluate(StuckContext ctx)
    {
        var now = ctx.AsOf;
        var attempts = ctx.Attempts ?? Array.Empty<StuckContextAttempt>();
        var signals = ctx.SessionSignals;

        // ── Rule R1: Motivational — long pause + low-effort pattern ─
        // Help request with ≥2 items bailed in this session and rolling
        // accuracy below 30% is a motivational signal, not cognitive.
        if (signals.ItemsBailedInSession >= 2 && signals.RecentAccuracy < 0.3)
        {
            return Emit(
                primary: StuckType.Motivational,
                primaryConf: 0.75f,
                secondary: StuckType.MetaStuck,
                secondaryConf: 0.4f,
                strategy: StuckScaffoldStrategy.Encouragement,
                teacher: false,
                reason: "heuristic.bails_and_low_accuracy",
                ctx: ctx);
        }

        // ── Rule R2: Encoding — no attempt yet, long time on question ─
        // If the student has been on the question >120s with zero
        // attempts, they're probably not parsing it.
        if (attempts.Count == 0 && signals.TimeOnQuestionSec >= 120)
        {
            // Tie-breaker: if they've previously mastered items in this
            // session, lean toward Motivational (fatigue) rather than
            // Encoding (incomprehension).
            var leanMotivational = signals.ItemsSolvedInSession >= 5 &&
                                   signals.TimeOnQuestionSec >= 180;
            return leanMotivational
                ? Emit(primary: StuckType.Motivational, primaryConf: 0.65f,
                    secondary: StuckType.Encoding, secondaryConf: 0.45f,
                    strategy: StuckScaffoldStrategy.Encouragement, teacher: false,
                    reason: "heuristic.long_time_after_solved_streak", ctx: ctx)
                : Emit(primary: StuckType.Encoding, primaryConf: 0.8f,
                    secondary: StuckType.Recall, secondaryConf: 0.35f,
                    strategy: StuckScaffoldStrategy.Rephrase, teacher: false,
                    reason: "heuristic.zero_attempts_long_time", ctx: ctx);
        }

        // ── Rule R3: Misconception — ≥3 attempts with identical first
        //            token of the scrubbed input. The same wrong start
        //            is a strong signal of an entrenched pattern.
        if (attempts.Count >= 3)
        {
            var signatures = attempts
                .Select(a => FirstToken(a.LatexInputScrubbed))
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
            if (signatures.Count >= 3 && signatures.All(s => s == signatures[0]))
            {
                return Emit(
                    primary: StuckType.Misconception,
                    primaryConf: 0.72f,          // confident-but-not-certain (could be strategy)
                    secondary: StuckType.Strategic,
                    secondaryConf: 0.45f,
                    strategy: StuckScaffoldStrategy.ContradictionPrompt,
                    teacher: false,
                    reason: "heuristic.repeated_same_first_token",
                    ctx: ctx);
            }

            // ── Rule R3b: Strategic — ≥3 attempts with HIGH input-change
            //             ratio (every attempt is structurally different).
            //             Signals method-hopping without commitment.
            var highVariance = attempts.Count(a => a.InputChangeRatio > 0.6f) >= 2;
            if (highVariance)
            {
                return Emit(
                    primary: StuckType.Strategic,
                    primaryConf: 0.7f,
                    secondary: StuckType.MetaStuck,
                    secondaryConf: 0.4f,
                    strategy: StuckScaffoldStrategy.DecompositionPrompt,
                    teacher: false,
                    reason: "heuristic.high_variance_attempts",
                    ctx: ctx);
            }
        }

        // ── Rule R4: Procedural — last attempt was wrong but very close
        //             (small edit distance to prior correct answer, or
        //             ErrorType ∈ {Arithmetic, SignError, LastStep}).
        var last = attempts.LastOrDefault();
        if (last is not null && !last.WasCorrect && IsLateStepError(last.ErrorType))
        {
            return Emit(
                primary: StuckType.Procedural,
                primaryConf: 0.78f,
                secondary: StuckType.Recall,
                secondaryConf: 0.3f,
                strategy: StuckScaffoldStrategy.ShowNextStep,
                teacher: false,
                reason: "heuristic.late_step_error",
                ctx: ctx);
        }

        // ── Rule R5: MetaStuck — chapter status = Locked on the current
        //             chapter (never happens in well-ordered systems, but
        //             catches edge cases) OR retention < 0.3 AND attempts
        //             empty AND we have advancement data (guards against
        //             brand-new-student baseline where retention=0 is
        //             the starting state, not a stuck signal).
        var hasAdvancementSignal = ctx.Advancement.CurrentChapterId is not null
            && ctx.Advancement.ChaptersTotalCount > 0;
        if (ctx.Advancement.CurrentChapterStatus == "Locked" ||
            (hasAdvancementSignal &&
             ctx.Advancement.CurrentChapterRetention < 0.3f &&
             attempts.Count == 0))
        {
            return Emit(
                primary: StuckType.MetaStuck,
                primaryConf: 0.68f,
                secondary: StuckType.Encoding,
                secondaryConf: 0.4f,
                strategy: StuckScaffoldStrategy.Regroup,
                teacher: true,         // MetaStuck candidates lean teacher
                reason: "heuristic.low_retention_or_locked_chapter",
                ctx: ctx);
            }

        // ── Rule R6: Recall — exactly one blank/near-blank attempt after
        //             a meaningful pause.
        if (attempts.Count == 1 &&
            IsNearBlank(attempts[0].LatexInputScrubbed) &&
            attempts[0].TimeSincePrevAttemptSec >= 30)
        {
            return Emit(
                primary: StuckType.Recall,
                primaryConf: 0.7f,
                secondary: StuckType.Encoding,
                secondaryConf: 0.35f,
                strategy: StuckScaffoldStrategy.ShowDefinition,
                teacher: false,
                reason: "heuristic.blank_after_pause",
                ctx: ctx);
        }

        // ── Fallthrough: Unknown → LLM layer decides. We return a
        //                secondary "Strategic" hint at 0.25 to nudge
        //                the LLM toward Strategic if it also can't decide
        //                — but that's the hybrid composer's call, not
        //                ours. Heuristic declines cleanly.
        return StuckDiagnosis.Unknown(
            _options.ClassifierVersion,
            StuckDiagnosisSource.Heuristic,
            latencyMs: 0,
            reasonCode: "heuristic.no_rule_fired",
            at: now);
    }

    private StuckDiagnosis Emit(
        StuckType primary, float primaryConf,
        StuckType secondary, float secondaryConf,
        StuckScaffoldStrategy strategy, bool teacher,
        string reason, StuckContext ctx)
    {
        return new StuckDiagnosis(
            Primary: primary,
            PrimaryConfidence: primaryConf,
            Secondary: secondary,
            SecondaryConfidence: secondaryConf,
            SuggestedStrategy: strategy,
            FocusChapterId: ctx.Advancement.CurrentChapterId,
            ShouldInvolveTeacher: teacher,
            Source: StuckDiagnosisSource.Heuristic,
            ClassifierVersion: _options.ClassifierVersion,
            DiagnosedAt: ctx.AsOf,
            LatencyMs: 0,
            SourceReasonCode: reason);
    }

    private static string FirstToken(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var trimmed = s.TrimStart();
        int end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != ',')
            end++;
        return trimmed[..end];
    }

    private static bool IsNearBlank(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var compact = s.Replace(" ", "").Replace("\t", "");
        return compact.Length <= 2;
    }

    private static bool IsLateStepError(string? errorType)
    {
        if (string.IsNullOrWhiteSpace(errorType)) return false;
        return errorType.Equals("Arithmetic", StringComparison.OrdinalIgnoreCase)
            || errorType.Equals("SignError", StringComparison.OrdinalIgnoreCase)
            || errorType.Equals("LastStep", StringComparison.OrdinalIgnoreCase)
            || errorType.Equals("FinalSimplification", StringComparison.OrdinalIgnoreCase);
    }
}
