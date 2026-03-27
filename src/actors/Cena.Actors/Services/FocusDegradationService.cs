// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus Degradation & Student Resilience Service
//
// Models student focus decay within and across sessions using:
// 1. Within-session attention curve (vigilance decrement theory)
// 2. Cross-session resilience scoring (grit/persistence measurement)
// 3. Productive struggle detection (distinguish fatigue from learning)
// 4. Recovery prediction (optimal break duration, re-engagement timing)
//
// References (all verified):
// - Warm, J.S. (1984). "An Introduction to Vigilance." In Warm (Ed.),
//   Sustained Attention in Human Performance. Wiley. [Foundational vigilance theory]
// - Parasuraman, R. (1986). "Vigilance, monitoring, and search." In Boff, Kaufman
//   & Thomas (Eds.), Handbook of Human Perception and Performance, Vol II. Wiley.
// - Kapur, M. (2008). "Productive Failure in Mathematical Problem Solving."
//   Cognition and Instruction, 26(3), 379-424. DOI: 10.1080/07370000802212669
// - Csikszentmihalyi, M. (1990). Flow: The Psychology of Optimal Experience.
//   Harper & Row. [Challenge/skill balance, 9 flow conditions]
// - Duckworth, A.L. et al. (2007). "Grit: Perseverance and Passion for Long-Term Goals."
//   J. Personality and Social Psychology, 92(6), 1087-1101. DOI: 10.1037/0022-3514.92.6.1087
//   NOTE: Grit has been critiqued — Crede et al. (2017) meta-analysis found weak
//   incremental validity over conscientiousness. Our resilience score is a composite,
//   not grit alone.
// - Esterman, M. et al. (2013). "In the Zone or Zoning Out? Tracking Behavioral and
//   Neural Fluctuations During Sustained Attention." Cerebral Cortex, 23(11), 2712-2723.
//   [RT variance as attention/focus proxy — used for our attention signal]
// - Wilson, D. & Conyers, M. (2020). "Developing Growth Mindsets Through Productive
//   Struggle." Edutopia. [Classroom application of productive failure theory]
//
// Cena-proprietary (no external citation):
// - Engagement-adjusted decay rate (3%/question baseline)
// - Remaining productive questions prediction model
// - Break duration linear interpolation formula
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics.Metrics;

namespace Cena.Actors.Services;

/// <summary>
/// Models student focus degradation and resilience.
/// Goes beyond simple fatigue detection to predict WHEN focus will drop
/// and distinguish productive struggle (learning) from unproductive frustration.
/// </summary>
public interface IFocusDegradationService
{
    /// <summary>
    /// Compute the current focus state from session signals.
    /// Called after each question to get real-time focus assessment.
    /// </summary>
    FocusState ComputeFocusState(FocusInput input);

    /// <summary>
    /// Predict how many more productive questions the student can handle.
    /// Returns 0 if the student should take a break NOW.
    /// </summary>
    int PredictRemainingProductiveQuestions(FocusState state);

    /// <summary>
    /// Compute the student's resilience score (long-term grit metric).
    /// Updated after each session based on how they handled difficulty.
    /// </summary>
    ResilienceScore ComputeResilience(ResilienceInput input);

    /// <summary>
    /// Determine if the student is in productive struggle (learning from difficulty)
    /// vs unproductive frustration (spinning wheels, about to disengage).
    /// </summary>
    StruggleClassification ClassifyStruggle(StruggleInput input);

    /// <summary>
    /// Predict optimal break duration based on focus history and time of day.
    /// </summary>
    BreakRecommendation RecommendBreak(FocusState state, TimeOfDayContext timeContext);
}

public sealed class FocusDegradationService : IFocusDegradationService
{
    // ACT-031: instance-based via IMeterFactory
    private readonly Histogram<double> _focusHistogram;

    public FocusDegradationService(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Cena.Actors.Focus", "1.0.0");
        _focusHistogram = meter.CreateHistogram<double>("cena.focus.score");
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. FOCUS STATE (within-session attention curve)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes a multi-dimensional focus state.
    /// The model uses 4 signals to build a complete picture:
    ///   - Attention (from response time variance — focused students are consistent)
    ///   - Engagement (from voluntary actions — hints, annotations, approach changes)
    ///   - Accuracy trend (slope of recent performance)
    ///   - Time decay (vigilance decrement — focus naturally drops over time)
    /// </summary>
    public FocusState ComputeFocusState(FocusInput input)
    {
        // ── Signal 1: Attention (response time consistency) ──
        // Focused students have LOW RT variance. Distracted students are erratic.
        double rtVariance = ComputeRtVariance(input.RecentResponseTimesMs);
        double baselineVariance = ComputeRtVariance(input.BaselineResponseTimesMs);
        double attentionScore = baselineVariance > 1.0
            ? Math.Clamp(1.0 - (rtVariance - baselineVariance) / (baselineVariance * 2), 0, 1)
            : 0.8; // Default if insufficient baseline

        // ── Signal 2: Engagement (voluntary interaction rate) ──
        // Students who request hints, add annotations, or change approach are engaged.
        // Students who just answer questions passively may be disengaged.
        double engagementRate = input.QuestionsAttempted > 0
            ? (double)(input.HintsRequested + input.AnnotationsAdded + input.ApproachChanges)
              / input.QuestionsAttempted
            : 0;
        // Normalize: 0.2 interactions/question = highly engaged
        double engagementScore = Math.Clamp(engagementRate / 0.2, 0, 1);

        // ── Signal 3: Accuracy Trend (slope over last 10 questions) ──
        // Rising accuracy = learning (good). Flat = plateau. Falling = degradation.
        double accuracySlope = ComputeAccuracySlope(input.RecentAccuracies);
        // Map slope to [0, 1]: -0.1 = 0 (declining), 0 = 0.5 (flat), +0.1 = 1 (improving)
        double trendScore = Math.Clamp(0.5 + accuracySlope * 5, 0, 1);

        // ── Signal 4: Vigilance Decrement (time-based natural decay) ──
        // Warm (1984): sustained attention degrades logarithmically over time.
        // Most students peak at ~10-15 minutes, then decline.
        double minutesActive = input.ElapsedMinutes;
        double peakMinutes = input.PersonalPeakMinutes > 0
            ? input.PersonalPeakMinutes
            : 15.0; // Default peak attention at 15 minutes

        // FOC-001.4: Executive load factor (Thomson 2022)
        // Higher Bloom levels → steeper vigilance decay because executive control
        // and vigilance decrements co-occur under cognitive load.
        double executiveLoad = input.ExecutiveLoadFactor ?? 0.0;

        double vigilanceScore;
        if (minutesActive <= peakMinutes)
        {
            // Ramping up to peak
            vigilanceScore = Math.Clamp(minutesActive / peakMinutes, 0.5, 1.0);
        }
        else
        {
            // Declining after peak (logarithmic decay)
            // Base decay coefficient: 0.3. Executive load adds up to 0.15 more.
            double decayCoefficient = 0.3 + executiveLoad * 0.15;
            double decayFactor = 1.0 - decayCoefficient * Math.Log(1.0 + (minutesActive - peakMinutes) / peakMinutes);
            vigilanceScore = Math.Clamp(decayFactor, 0.1, 1.0);
        }

        // ── FOC-001.3: Sensor signal scores (default to 0 when absent) ──
        double motionScore = input.MotionStabilityScore ?? 0;
        double appFocusScoreVal = input.AppFocusScore ?? 0;
        double touchScore = input.TouchPatternScore ?? 0;
        double envScore = input.EnvironmentScore ?? 0;

        // ── Adaptive weights (FOC-001.2) ──
        var sensorAvailability = FocusWeightCalculator.FromInput(input);
        var weights = FocusWeightCalculator.ComputeWeights(sensorAvailability);

        // ── Composite Focus Score ──
        double focusScore =
            weights.Attention * attentionScore +
            weights.Engagement * engagementScore +
            weights.Trend * trendScore +
            weights.Vigilance * vigilanceScore +
            weights.Motion * motionScore +
            weights.AppFocus * appFocusScoreVal +
            weights.TouchPattern * touchScore +
            weights.Environment * envScore;

        focusScore = Math.Clamp(focusScore, 0, 1);

        // ── FOC-004: Mind-wandering adjustment ──
        // AwareDrift: student self-corrected, reduce penalty by 50%
        // UnawareDrift: full penalty (no adjustment — score already reflects degradation)
        MindWanderingState? mindWanderingState = input.MindWanderingState;
        if (mindWanderingState == MindWanderingState.AwareDrift && focusScore < 0.6)
        {
            // Student drifted but came back — soften the focus penalty
            // Move score 50% of the way back toward 0.6 (Engaged threshold)
            focusScore = focusScore + (0.6 - focusScore) * 0.5;
            focusScore = Math.Clamp(focusScore, 0, 1);
        }

        // Tag telemetry with sensor count for analysis
        int sensorCount = input.SensorSignalCount;
        _focusHistogram.Record(focusScore, new KeyValuePair<string, object?>("sensor_count", sensorCount));

        // Confidence boost: more signals → more reliable assessment
        // Each sensor signal adds 5% confidence (max 20% boost with all 4)
        double confidenceBoost = sensorCount * 0.05;

        // ── Classify focus level ──
        var level = focusScore switch
        {
            >= 0.8 => FocusLevel.Flow,           // In the zone — challenge appropriately
            >= 0.6 => FocusLevel.Engaged,         // Good — maintain current approach
            >= 0.4 => FocusLevel.Drifting,         // Attention wavering — simplify or change method
            >= 0.2 => FocusLevel.Fatigued,         // Significantly degraded — suggest break soon
            _ => FocusLevel.Disengaged             // Lost — end session, suggest break
        };

        return new FocusState(
            FocusScore: focusScore,
            Level: level,
            AttentionScore: attentionScore,
            EngagementScore: engagementScore,
            TrendScore: trendScore,
            VigilanceScore: vigilanceScore,
            MinutesActive: minutesActive,
            QuestionsAttempted: input.QuestionsAttempted,
            SensorSignalCount: sensorCount,
            SensorConfidenceBoost: confidenceBoost,
            MindWandering: mindWanderingState
        );
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. REMAINING PRODUCTIVE QUESTIONS
    // ═══════════════════════════════════════════════════════════════

    public int PredictRemainingProductiveQuestions(FocusState state)
    {
        // Below engagement threshold — stop now
        if (state.Level is FocusLevel.Disengaged or FocusLevel.DisengagedExhausted or FocusLevel.DisengagedBored)
            return 0;
        if (state.Level == FocusLevel.Fatigued) return 1; // One more, then break

        // Estimate based on vigilance decay rate
        // Assume ~2 minutes per question, predict how many before focus < 0.4 (Drifting)
        double currentFocus = state.FocusScore;
        double decayPerQuestion = 0.03; // ~3% focus loss per question at steady state

        // Adjust decay by engagement (engaged students decay slower)
        decayPerQuestion *= (1.0 - state.EngagementScore * 0.5);

        int remaining = 0;
        while (currentFocus >= 0.4 && remaining < 20) // Cap at 20
        {
            currentFocus -= decayPerQuestion;
            remaining++;
        }

        return remaining;
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. RESILIENCE SCORING (cross-session grit)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Resilience = how well the student persists through difficulty.
    /// High resilience students: continue after wrong answers, try harder problems,
    /// return after breaks, maintain streaks through challenging material.
    /// </summary>
    public ResilienceScore ComputeResilience(ResilienceInput input)
    {
        // ── Persistence: ratio of sessions completed vs abandoned ──
        double persistence = input.TotalSessionsStarted > 0
            ? (double)input.SessionsCompletedNormally / input.TotalSessionsStarted
            : 0.5; // Default for new students

        // ── Recovery: how quickly they return after a bad session ──
        // Bad session = fatigue > 0.7 or accuracy < 30%
        double recoveryRate = input.BadSessionCount > 0
            ? (double)input.ReturnedAfterBadSession / input.BadSessionCount
            : 1.0; // No bad sessions = assume resilient

        // ── Challenge seeking: do they attempt harder problems? ──
        // Students who stay on "recall" level vs those who progress to "analysis"
        double challengeRatio = input.TotalAttempts > 0
            ? (double)input.AttemptsAboveComfortZone / input.TotalAttempts
            : 0.3; // Default

        // ── Streak maintenance: consistency over time ──
        double streakScore = input.LongestStreak > 0
            ? Math.Clamp((double)input.CurrentStreak / input.LongestStreak, 0, 1)
            : 0;

        // ── FOC-012: Culturally-adjusted resilience weights ──
        // ArabicDominant students: higher Recovery weight (collectivist resilience
        // manifests as returning after difficulty with peer support).
        // HebrewDominant/default: existing individualist-calibrated weights.
        var (wPersistence, wRecovery, wChallenge, wStreak) = input.CulturalContext switch
        {
            CulturalContext.ArabicDominant => (0.30, 0.30, 0.25, 0.15),
            _ => (0.35, 0.25, 0.25, 0.15)
        };

        // ── Composite resilience ──
        double score =
            wPersistence * persistence +
            wRecovery * recoveryRate +
            wChallenge * challengeRatio +
            wStreak * streakScore;

        score = Math.Clamp(score, 0, 1);

        var level = score switch
        {
            >= 0.8 => ResilienceLevel.HighGrit,
            >= 0.6 => ResilienceLevel.Moderate,
            >= 0.4 => ResilienceLevel.Developing,
            _ => ResilienceLevel.AtRisk
        };

        return new ResilienceScore(
            Score: score,
            Level: level,
            Persistence: persistence,
            RecoveryRate: recoveryRate,
            ChallengeSeeking: challengeRatio,
            StreakConsistency: streakScore
        );
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. PRODUCTIVE STRUGGLE vs FRUSTRATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Distinguishes productive struggle (Kapur 2008) from unproductive frustration.
    ///
    /// PRODUCTIVE: Student is wrong but LEARNING — accuracy is low but improving,
    ///   response times are consistent (engaged), error types are VARIED (exploring).
    ///
    /// UNPRODUCTIVE: Student is wrong and STUCK — accuracy flat, RT increasing or
    ///   highly variable, same error type repeating, annotation sentiment negative.
    /// </summary>
    public StruggleClassification ClassifyStruggle(StruggleInput input)
    {
        // ── Is accuracy improving despite being low? ──
        bool accuracyImproving = input.AccuracySlope > 0.01;

        // ── Are errors diverse (exploring) or repetitive (stuck)? ──
        bool errorsRepetitive = input.SameErrorTypeCount >= 3;

        // ── Is response time stable (engaged) or erratic (distracted)? ──
        double rtCv = input.ResponseTimeMean > 0
            ? input.ResponseTimeStdDev / input.ResponseTimeMean
            : 0;
        bool rtStable = rtCv < 0.4; // Coefficient of variation < 40%

        // ── Is sentiment negative? ──
        bool sentimentNegative = input.AnnotationSentiment < 0.3;

        // ── FOC-005: Confusion-aware classification (checked first) ──
        // D'Mello & Graesser (2014): confusion is beneficial IF it resolves.
        if (input.ConfusionState.HasValue)
        {
            switch (input.ConfusionState.Value)
            {
                case ConfusionState.ConfusionResolving:
                    // Student is confused but working through it — DO NOT intervene
                    return new StruggleClassification(
                        Type: StruggleType.ProductiveConfusion,
                        Confidence: 0.85,
                        Recommendation: "Student is confused but resolving. Provide NO hint. " +
                                        "Wait for resolution window to expire. " +
                                        "Confusion that resolves produces deep learning."
                    );

                case ConfusionState.ConfusionStuck:
                    // Confusion persisted past patience window — scaffold, don't restart
                    return new StruggleClassification(
                        Type: StruggleType.UnproductiveFrustration,
                        Confidence: 0.85,
                        Recommendation: "Confusion persisted past patience window. " +
                                        "Provide scaffolding hint, not methodology switch. " +
                                        "Confusion needs a nudge, not a restart."
                    );

                case ConfusionState.Confused:
                    // Fresh confusion — monitor, don't intervene yet
                    return new StruggleClassification(
                        Type: StruggleType.ProductiveConfusion,
                        Confidence: 0.6,
                        Recommendation: "Confusion just detected. Monitor for resolution. " +
                                        "Do NOT intervene yet — give the student time to work through it."
                    );
            }
        }

        // ── FOC-004: Mind-wandering-aware classification ──
        // Only AwareDrift and UnawareDrift produce specific interventions.
        // Focused/Ambiguous fall through to standard classification.
        if (input.MindWanderingState is MindWanderingState.AwareDrift)
        {
            return new StruggleClassification(
                Type: StruggleType.ModerateStruggle,
                Confidence: 0.75,
                Recommendation: FocusMessages.WelcomeBackNudge() +
                                " Student self-corrected from mind-wandering. " +
                                "Gentle nudge only — no methodology change needed."
            );
        }

        if (input.MindWanderingState is MindWanderingState.UnawareDrift)
        {
            return new StruggleClassification(
                Type: StruggleType.Disengagement,
                Confidence: 0.8,
                Recommendation: "Unaware mind-wandering detected. Change question type " +
                                "or add visual stimulus to re-engage. If persistent, trigger microbreak."
            );
        }

        // ── Standard classification (no confusion data or NotConfused) ──

        // ── FOC-008: Solution diversity boosts productive struggle detection ──
        int diversityCount = input.SolutionDiversityCount ?? 1;
        bool diversityCompensates = diversityCount >= 3 && input.AccuracySlope > -0.05;

        if ((accuracyImproving || diversityCompensates) && !errorsRepetitive && rtStable)
        {
            double confidence = 0.8 + (input.AccuracySlope * 2) + (diversityCount * 0.05);
            return new StruggleClassification(
                Type: StruggleType.ProductiveStruggle,
                Confidence: Math.Clamp(confidence, 0.5, 0.99),
                Recommendation: "Student is learning through difficulty. Maintain current approach. " +
                                "Do NOT switch methodology or reduce difficulty — this is productive failure."
            );
        }

        if (!accuracyImproving && errorsRepetitive && sentimentNegative)
        {
            return new StruggleClassification(
                Type: StruggleType.UnproductiveFrustration,
                Confidence: 0.9,
                Recommendation: "Student is stuck. Switch methodology (methodology switch service) " +
                                "or reduce difficulty. If methodology already switched 3+ times, " +
                                "suggest a different concept (prerequisite review or adjacent topic)."
            );
        }

        if (!accuracyImproving && !rtStable)
        {
            return new StruggleClassification(
                Type: StruggleType.Disengagement,
                Confidence: 0.7,
                Recommendation: "Student is losing attention. Suggest a break or switch to a " +
                                "gamified challenge (challenge card) to re-engage."
            );
        }

        // Mixed signals — default to moderate struggle
        return new StruggleClassification(
            Type: StruggleType.ModerateStruggle,
            Confidence: 0.5,
            Recommendation: "Signals are mixed. Continue current approach for 2 more questions, " +
                            "then re-evaluate. If accuracy doesn't improve, switch methodology."
        );
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. BREAK RECOMMENDATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Two-tier break system (FOC-003.3):
    ///   Tier 1 — Proactive microbreak: scheduled BEFORE focus drops (60-90s)
    ///   Tier 2 — Reactive recovery break: triggered AFTER focus degrades (5-30 min)
    ///
    /// Microbreak decisions come from IMicrobreakScheduler.
    /// This method handles reactive recovery breaks only.
    /// Use RecommendBreakTwoTier() for the combined decision.
    /// </summary>
    public BreakRecommendation RecommendBreak(FocusState state, TimeOfDayContext timeContext)
    {
        return RecommendRecoveryBreak(state, timeContext);
    }

    /// <summary>
    /// Combined two-tier decision: checks microbreak first, then reactive.
    /// </summary>
    public BreakRecommendation RecommendBreakTwoTier(
        FocusState state, TimeOfDayContext timeContext,
        IMicrobreakScheduler microbreakScheduler, MicrobreakContext microbreakContext)
    {
        // ── Tier 1: Proactive microbreak (check first) ──
        var microbreakDecision = microbreakScheduler.ShouldTrigger(microbreakContext);
        if (microbreakDecision.ShouldBreak)
        {
            return new BreakRecommendation(
                ShouldBreak: true,
                Minutes: 0, // Microbreaks are measured in seconds
                DurationSeconds: microbreakDecision.DurationSeconds,
                Activity: BreakActivity.StretchOrWater, // Generic; actual activity in MicrobreakDecision
                BreakType: BreakType.Microbreak,
                MicrobreakActivity: microbreakDecision.Activity,
                Message: microbreakDecision.Message
            );
        }

        // ── Tier 2: Reactive recovery break (existing logic) ──
        return RecommendRecoveryBreak(state, timeContext);
    }

    private BreakRecommendation RecommendRecoveryBreak(FocusState state, TimeOfDayContext timeContext)
    {
        // ── FOC-006: Bored students need challenge, NOT break ──
        if (state.Level == FocusLevel.DisengagedBored)
        {
            return new BreakRecommendation(
                ShouldBreak: false,
                Minutes: 0,
                Activity: BreakActivity.None,
                Message: FocusMessages.BoredNeedsChallenge(),
                AlternativeAction: AlternativeAction.IncreaseDifficulty
            );
        }

        // Base break duration from focus score
        int baseMinutes = state.Level switch
        {
            FocusLevel.Flow => 0,                   // Don't interrupt flow state!
            FocusLevel.Engaged => 0,                 // No break needed
            FocusLevel.Drifting => 5,                 // Short break to reset attention
            FocusLevel.Fatigued => 15,                // Moderate break
            FocusLevel.DisengagedExhausted => 30,     // Long break — exhausted
            FocusLevel.Disengaged => 30,              // Long break — unclassified disengagement
            _ => 10
        };

        if (baseMinutes == 0)
        {
            return new BreakRecommendation(
                ShouldBreak: false,
                Minutes: 0,
                Activity: BreakActivity.None,
                Message: null
            );
        }

        // Adjust for time of day (late evening = longer breaks needed)
        if (timeContext.IsLateEvening) // After 21:00
            baseMinutes = (int)(baseMinutes * 1.5);

        // Adjust for session count today (more sessions = more fatigued)
        if (timeContext.SessionsToday >= 3)
            baseMinutes = (int)(baseMinutes * 1.3);

        // Suggest activity based on break length
        var activity = baseMinutes switch
        {
            <= 5 => BreakActivity.StretchOrWater,
            <= 15 => BreakActivity.WalkOrSnack,
            _ => BreakActivity.FullRest
        };

        // Hebrew message
        string message = state.Level switch
        {
            FocusLevel.Drifting => FocusMessages.BreakDrifting(),
            FocusLevel.Fatigued => FocusMessages.BreakFatigued(),
            FocusLevel.DisengagedExhausted => FocusMessages.BreakExhausted(),
            FocusLevel.Disengaged => FocusMessages.BreakDisengaged(),
            _ => FocusMessages.BreakGeneric()
        };

        return new BreakRecommendation(
            ShouldBreak: true,
            Minutes: baseMinutes,
            Activity: activity,
            BreakType: BreakType.RecoveryBreak,
            Message: message
        );
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static double ComputeRtVariance(IReadOnlyList<double> times)
    {
        if (times.Count < 2) return 0;
        double mean = 0;
        for (int i = 0; i < times.Count; i++) mean += times[i];
        mean /= times.Count;

        double variance = 0;
        for (int i = 0; i < times.Count; i++)
        {
            double diff = times[i] - mean;
            variance += diff * diff;
        }
        return variance / (times.Count - 1);
    }

    private static double ComputeAccuracySlope(IReadOnlyList<double> accuracies)
    {
        if (accuracies.Count < 3) return 0;

        // Simple linear regression slope
        int n = accuracies.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += accuracies[i];
            sumXY += i * accuracies[i];
            sumX2 += i * i;
        }

        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 0.0001) return 0;

        return (n * sumXY - sumX * sumY) / denominator;
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public record FocusInput(
    IReadOnlyList<double> RecentResponseTimesMs,
    IReadOnlyList<double> BaselineResponseTimesMs,
    IReadOnlyList<double> RecentAccuracies,
    double ElapsedMinutes,
    int QuestionsAttempted,
    int HintsRequested,
    int AnnotationsAdded,
    int ApproachChanges,
    double PersonalPeakMinutes, // 0 = use default (15 min)
    // ── Sensor signals (FOC-001.1) ──
    // All nullable: absence means no sensor data available (web client, permissions denied).
    // When null, the existing 4-signal model is used unchanged.
    double? MotionStabilityScore = null,   // 0-1: accelerometer/gyroscope stability (1 = still/focused)
    double? AppFocusScore = null,          // 0-1: app lifecycle continuity (1 = no backgrounding)
    double? TouchPatternScore = null,      // 0-1: tap rhythm consistency (1 = consistent touch)
    double? EnvironmentScore = null,       // 0-1: ambient light/proximity stability (1 = stable)
    // ── Executive load (FOC-001.4) ──
    double? ExecutiveLoadFactor = null,     // 0-1: task complexity (recall=0, application=0.3, analysis=0.6, synthesis=0.8)
    // ── Mind-wandering (FOC-004) ──
    MindWanderingState? MindWanderingState = null  // Pre-computed by IMindWanderingDetector; null = not available
)
{
    /// <summary>True if any mobile sensor signal is available.</summary>
    public bool SensorDataAvailable =>
        MotionStabilityScore.HasValue || AppFocusScore.HasValue ||
        TouchPatternScore.HasValue || EnvironmentScore.HasValue;

    /// <summary>Count of available sensor signals (0-4).</summary>
    public int SensorSignalCount =>
        (MotionStabilityScore.HasValue ? 1 : 0) +
        (AppFocusScore.HasValue ? 1 : 0) +
        (TouchPatternScore.HasValue ? 1 : 0) +
        (EnvironmentScore.HasValue ? 1 : 0);
}

public record FocusState(
    double FocusScore,
    FocusLevel Level,
    double AttentionScore,
    double EngagementScore,
    double TrendScore,
    double VigilanceScore,
    double MinutesActive,
    int QuestionsAttempted,
    // ── Sensor enrichment (FOC-001.3) ──
    int SensorSignalCount = 0,           // 0-4: how many sensor signals contributed
    double SensorConfidenceBoost = 0.0,  // Higher signal count → higher confidence in assessment
    // ── Mind-wandering (FOC-004) ──
    MindWanderingState? MindWandering = null  // null = detector not available
);

public enum FocusLevel
{
    Flow,                // 0.8+ — in the zone, challenge appropriately
    Engaged,             // 0.6-0.8 — good, maintain
    Drifting,            // 0.4-0.6 — attention wavering, simplify or change
    Fatigued,            // 0.2-0.4 — cognitive/physical fatigue, suggest break soon
    Disengaged,          // <0.2 — end session (unclassified disengagement)
    DisengagedBored,     // FOC-006: <0.2 + boredom signals — increase challenge, NOT break
    DisengagedExhausted  // FOC-006: <0.2 + fatigue signals — take break, rest
}

public record ResilienceInput(
    int TotalSessionsStarted,
    int SessionsCompletedNormally,
    int BadSessionCount,
    int ReturnedAfterBadSession,
    int TotalAttempts,
    int AttemptsAboveComfortZone,
    int CurrentStreak,
    int LongestStreak,
    // ── FOC-012: Cultural context for weight adjustment ──
    CulturalContext CulturalContext = CulturalContext.Unknown
);

public record ResilienceScore(
    double Score,
    ResilienceLevel Level,
    double Persistence,
    double RecoveryRate,
    double ChallengeSeeking,
    double StreakConsistency
);

public enum ResilienceLevel
{
    HighGrit,     // 0.8+ — persists through difficulty, seeks challenge
    Moderate,     // 0.6-0.8 — generally persistent
    Developing,   // 0.4-0.6 — needs encouragement
    AtRisk        // <0.4 — may disengage; needs gamification + easier wins
}

public record StruggleInput(
    double AccuracySlope,
    int SameErrorTypeCount,
    double ResponseTimeMean,
    double ResponseTimeStdDev,
    double AnnotationSentiment,
    // ── FOC-005: Confusion state (optional — null = no confusion detection available) ──
    ConfusionState? ConfusionState = null,
    // ── FOC-004: Mind-wandering state (optional — null = detector not available) ──
    MindWanderingState? MindWanderingState = null,
    // ── FOC-008: Solution diversity (optional — null = tracker not available) ──
    int? SolutionDiversityCount = null
);

public record StruggleClassification(
    StruggleType Type,
    double Confidence,
    string Recommendation
);

public enum StruggleType
{
    ProductiveStruggle,       // Learning from difficulty — DON'T intervene
    ProductiveConfusion,      // FOC-005: Confused but resolving — provide NO hint, wait
    ModerateStruggle,         // Mixed signals — observe 2 more questions
    UnproductiveFrustration,  // Stuck — switch methodology or reduce difficulty
    Disengagement             // Lost attention — suggest break or gamified challenge
}

public record BreakRecommendation(
    bool ShouldBreak,
    int Minutes,
    BreakActivity Activity,
    string? Message,
    // ── FOC-003: Two-tier break system ──
    BreakType BreakType = BreakType.RecoveryBreak,
    int DurationSeconds = 0,                          // For microbreaks (60-90s)
    MicrobreakActivity? MicrobreakActivity = null,    // Specific microbreak activity
    // ── FOC-006: Alternative actions for bored students (breaks won't help boredom) ──
    AlternativeAction AlternativeAction = AlternativeAction.None
);

/// <summary>
/// Proactive = scheduled microbreak BEFORE focus drops (Cohen's d = 1.784).
/// Reactive  = recovery break AFTER focus has degraded (existing behavior).
/// </summary>
public enum BreakType
{
    RecoveryBreak,  // Reactive: 5-30 min, triggered by low focus score
    Microbreak      // Proactive: 60-90s, scheduled by MicrobreakScheduler
}

public enum BreakActivity
{
    None,
    StretchOrWater,
    WalkOrSnack,
    FullRest
}

public record TimeOfDayContext(
    bool IsLateEvening,  // After 21:00 Israel time
    int SessionsToday
);
