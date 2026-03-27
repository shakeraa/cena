// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Mind-Wandering Detector (FOC-004.1)
//
// Distinguishes aware from unaware mind-wandering using response time
// patterns. Wammes et al. (2022) meta-analysis: mind-wandering occurs
// ~30% of educational time, explains ~7% of learning outcome variance.
//
// Two types:
//   Aware drift:   student notices they drifted (pause → return)
//                  → gentle nudge, 50% reduced focus penalty
//   Unaware drift: student doesn't notice (gradual RT degradation)
//                  → stronger intervention, full focus penalty
//
// Detection is purely behavioral — no biometrics needed.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Detects mind-wandering state from response time patterns.
/// </summary>
public interface IMindWanderingDetector
{
    MindWanderingResult Detect(MindWanderingInput input);
}

public sealed class MindWanderingDetector : IMindWanderingDetector
{
    // A single RT > 2x baseline mean = "gap" (pause)
    private const double GapThreshold = 2.0;

    // Minimum questions needed to detect unaware drift
    private const int MinQuestionsForDrift = 5;

    // RT variance increase ratio that signals gradual drift
    private const double VarianceIncreaseThreshold = 1.5;

    public MindWanderingResult Detect(MindWanderingInput input)
    {
        if (input.RecentRtMs.Count < 3 || input.BaselineRtMs.Count < 2)
        {
            return new MindWanderingResult(
                State: MindWanderingState.Focused,
                Confidence: 0.5,
                GapIndex: null
            );
        }

        double baselineMean = Mean(input.BaselineRtMs);
        double baselineVariance = Variance(input.BaselineRtMs, baselineMean);

        // ── Check for aware drift: single RT gap followed by recovery ──
        int? gapIndex = FindGapIndex(input.RecentRtMs, baselineMean);

        if (gapIndex.HasValue)
        {
            // Check if post-gap RTs returned to baseline range
            bool recovered = CheckRecoveryAfterGap(input.RecentRtMs, gapIndex.Value, baselineMean);

            if (recovered)
            {
                // Aware drift: student paused, then came back
                double confidence = ComputeAwareConfidence(
                    input.RecentRtMs, gapIndex.Value, baselineMean);

                return new MindWanderingResult(
                    State: MindWanderingState.AwareDrift,
                    Confidence: confidence,
                    GapIndex: gapIndex.Value
                );
            }
        }

        // ── Check for unaware drift: gradual RT increase over 5+ questions ──
        if (input.RecentRtMs.Count >= MinQuestionsForDrift)
        {
            var (isDrifting, driftConfidence) = CheckGradualDrift(
                input.RecentRtMs, baselineMean, baselineVariance);

            if (isDrifting)
            {
                return new MindWanderingResult(
                    State: MindWanderingState.UnawareDrift,
                    Confidence: driftConfidence,
                    GapIndex: null
                );
            }
        }

        // ── Check if RT variance is elevated but pattern is ambiguous ──
        double recentVariance = Variance(input.RecentRtMs, Mean(input.RecentRtMs));
        if (baselineVariance > 1.0 && recentVariance > baselineVariance * VarianceIncreaseThreshold)
        {
            return new MindWanderingResult(
                State: MindWanderingState.Ambiguous,
                Confidence: 0.4,
                GapIndex: null
            );
        }

        // ── Focused: RT variance within baseline, no gaps ──
        return new MindWanderingResult(
            State: MindWanderingState.Focused,
            Confidence: Math.Clamp(1.0 - recentVariance / (baselineVariance * 3 + 1), 0.5, 1.0),
            GapIndex: null
        );
    }

    /// <summary>
    /// Find the index of a single large RT gap (>2x baseline mean).
    /// Returns null if no gap or multiple gaps (multiple gaps = different pattern).
    /// </summary>
    private static int? FindGapIndex(IReadOnlyList<double> rtMs, double baselineMean)
    {
        double threshold = baselineMean * GapThreshold;
        int? gapIdx = null;

        for (int i = 0; i < rtMs.Count; i++)
        {
            if (rtMs[i] > threshold)
            {
                if (gapIdx.HasValue)
                    return null; // Multiple gaps — not the aware pattern
                gapIdx = i;
            }
        }

        return gapIdx;
    }

    /// <summary>
    /// After a gap, check that at least 1 subsequent RT is within 1.5x baseline.
    /// </summary>
    private static bool CheckRecoveryAfterGap(
        IReadOnlyList<double> rtMs, int gapIndex, double baselineMean)
    {
        double recoveryThreshold = baselineMean * 1.5;
        int recoveredCount = 0;

        for (int i = gapIndex + 1; i < rtMs.Count; i++)
        {
            if (rtMs[i] <= recoveryThreshold)
                recoveredCount++;
        }

        return recoveredCount >= 1;
    }

    /// <summary>
    /// Confidence for aware drift: bigger gap + faster recovery = higher confidence.
    /// </summary>
    private static double ComputeAwareConfidence(
        IReadOnlyList<double> rtMs, int gapIndex, double baselineMean)
    {
        double gapRatio = rtMs[gapIndex] / baselineMean;
        // Gap of 2x = base confidence 0.6, gap of 4x+ = confidence 0.9
        double gapConfidence = Math.Clamp(0.4 + gapRatio * 0.1, 0.6, 0.95);

        // Check how quickly they recovered
        int postGapCount = rtMs.Count - gapIndex - 1;
        if (postGapCount >= 2)
            gapConfidence = Math.Min(gapConfidence + 0.1, 0.95);

        return gapConfidence;
    }

    /// <summary>
    /// Detect gradual drift: monotonically increasing RT trend over 5+ questions
    /// with no single large gap (the "boiling frog" pattern).
    /// </summary>
    private static (bool IsDrifting, double Confidence) CheckGradualDrift(
        IReadOnlyList<double> rtMs, double baselineMean, double baselineVariance)
    {
        // Compute slope of RT over the recent window using linear regression
        double slope = ComputeSlope(rtMs);

        // Positive slope means RTs are increasing (getting slower)
        // Normalize by baseline mean: slope of 100ms/question on a 2000ms baseline = 5%
        double normalizedSlope = baselineMean > 0 ? slope / baselineMean : 0;

        // Need meaningful positive slope (>3% per question)
        if (normalizedSlope < 0.03)
            return (false, 0);

        // Check that no single consecutive jump exceeds the gap threshold
        // (a sudden jump is the aware pattern, gradual increase is unaware)
        for (int i = 1; i < rtMs.Count; i++)
        {
            double jump = rtMs[i] - rtMs[i - 1];
            if (jump > baselineMean * (GapThreshold - 1.0)) // >100% of baseline in one step
                return (false, 0);
        }

        // Confidence based on how strong the upward trend is
        double confidence = Math.Clamp(0.5 + normalizedSlope * 5, 0.6, 0.95);
        return (true, confidence);
    }

    private static double ComputeSlope(IReadOnlyList<double> values)
    {
        int n = values.Count;
        if (n < 2) return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += (double)i * i;
        }

        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 0.0001) return 0;

        return (n * sumXY - sumX * sumY) / denominator;
    }

    private static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < values.Count; i++) sum += values[i];
        return sum / values.Count;
    }

    private static double Variance(IReadOnlyList<double> values, double mean)
    {
        if (values.Count < 2) return 0;
        double sum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            double diff = values[i] - mean;
            sum += diff * diff;
        }
        return sum / (values.Count - 1);
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public enum MindWanderingState
{
    Focused,      // RT variance within baseline, no gaps — student is on task
    AwareDrift,   // Single RT gap then recovery — student self-corrected
    UnawareDrift, // Gradual RT increase over 5+ questions — student doesn't realize
    Ambiguous     // Elevated variance but pattern doesn't match aware or unaware
}

public record MindWanderingInput(
    IReadOnlyList<double> RecentRtMs,       // Last 5-10 response times in milliseconds
    IReadOnlyList<double> BaselineRtMs,     // Baseline RTs for comparison (from focused period)
    IReadOnlyList<double> RecentAccuracies  // Accuracy per question (0 or 1) for context
);

public record MindWanderingResult(
    MindWanderingState State,
    double Confidence,    // 0-1: how certain we are about this classification
    int? GapIndex         // For AwareDrift: which question had the gap (null otherwise)
);
