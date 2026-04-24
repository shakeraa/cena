// =============================================================================
// Cena Platform -- CognitiveLoadService (Domain Service)
// Layer: Domain Services | Runtime: .NET 9
//
// Fatigue formula: fatigue = 0.4*accuracy_drop + 0.3*rt_increase + 0.3*time_fraction
// Computes fatigue assessment, cooldown duration, and difficulty adjustment
// recommendations based on real-time cognitive load signals.
// =============================================================================

using System.Runtime.CompilerServices;

namespace Cena.Actors.Services;

// =============================================================================
// RESULT TYPES
// =============================================================================

/// <summary>
/// Full fatigue assessment result including component scores and recommendations.
/// </summary>
public sealed record FatigueAssessment(
    double FatigueScore,
    double AccuracyDropComponent,
    double RtIncreaseComponent,
    double TimeFractionComponent,
    bool IsHighFatigue,
    string Level);

/// <summary>
/// Difficulty adjustment recommendation based on fatigue level.
/// </summary>
public enum DifficultyAdjustment
{
    /// <summary>Reduce difficulty to prevent cognitive overload.</summary>
    Ease,

    /// <summary>Maintain current difficulty level.</summary>
    Maintain,

    /// <summary>Increase difficulty to maintain engagement.</summary>
    Increase
}

// =============================================================================
// INTERFACE
// =============================================================================

public interface ICognitiveLoadService
{
    /// <summary>
    /// Compute a fatigue assessment from real-time session signals.
    /// Formula: fatigue = 0.4 * accuracy_drop + 0.3 * rt_increase + 0.3 * time_fraction
    /// </summary>
    /// <param name="baselineAccuracy">Student's trailing-window baseline accuracy (0-1).</param>
    /// <param name="rollingAccuracy5">Rolling average accuracy over last 5 questions (0-1).</param>
    /// <param name="baselineRt">Baseline response time in ms.</param>
    /// <param name="rollingRt5">Rolling average response time over last 5 questions in ms.</param>
    /// <param name="elapsedMin">Minutes elapsed in current session.</param>
    /// <param name="maxSessionMin">Maximum session duration in minutes.</param>
    /// <returns>Full fatigue assessment with component breakdown.</returns>
    FatigueAssessment ComputeFatigue(
        double baselineAccuracy,
        double rollingAccuracy5,
        double baselineRt,
        double rollingRt5,
        double elapsedMin,
        double maxSessionMin);

    /// <summary>
    /// Compute recommended cooldown duration in minutes based on fatigue score.
    /// Higher fatigue = longer cooldown. Range: 5-30 minutes.
    /// </summary>
    /// <param name="fatigueScore">Fatigue score in [0, 1].</param>
    /// <returns>Cooldown duration in minutes (5-30).</returns>
    int ComputeCooldownMinutes(double fatigueScore);

    /// <summary>
    /// Recommend whether to ease, maintain, or increase difficulty based on
    /// the current fatigue score and difficulty level.
    /// </summary>
    /// <param name="fatigueScore">Current fatigue score in [0, 1].</param>
    /// <param name="currentDifficulty">Current difficulty level (1-10 scale).</param>
    /// <returns>Difficulty adjustment recommendation.</returns>
    DifficultyAdjustment RecommendDifficultyAdjustment(double fatigueScore, int currentDifficulty);
}

// =============================================================================
// IMPLEMENTATION
// =============================================================================

/// <summary>
/// Production cognitive load service implementing the 3-factor weighted fatigue model.
///
/// Factor weights (from system-overview.md):
///   W1 = 0.4 (accuracy drop from baseline)
///   W2 = 0.3 (response time increase from baseline)
///   W3 = 0.3 (session time fraction)
///
/// Fatigue thresholds:
///   Low:      [0.0, 0.3)
///   Moderate: [0.3, 0.6)
///   High:     [0.6, 0.8)
///   Critical: [0.8, 1.0]
///
/// Cooldown mapping (linear interpolation):
///   fatigueScore=0.0 -> 5 minutes
///   fatigueScore=1.0 -> 30 minutes
///
/// Difficulty adjustment:
///   fatigue >= 0.7 -> Ease
///   fatigue <= 0.3 AND currentDifficulty < 8 -> Increase
///   otherwise -> Maintain
/// </summary>
public sealed class CognitiveLoadService : ICognitiveLoadService
{
    // Fatigue formula weights
    private const double W1_AccuracyDrop = 0.4;
    private const double W2_RtIncrease = 0.3;
    private const double W3_TimeFraction = 0.3;

    // Fatigue level thresholds
    private const double HighFatigueThreshold = 0.6;

    // Cooldown range (minutes)
    private const int MinCooldownMinutes = 5;
    private const int MaxCooldownMinutes = 30;

    // Difficulty adjustment thresholds
    private const double EaseThreshold = 0.7;
    private const double IncreaseThreshold = 0.3;
    private const int MaxDifficultyForIncrease = 8;

    // Minimum denominators to prevent division by zero
    private const double MinDenominator = 0.001;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FatigueAssessment ComputeFatigue(
        double baselineAccuracy,
        double rollingAccuracy5,
        double baselineRt,
        double rollingRt5,
        double elapsedMin,
        double maxSessionMin)
    {
        // ── Signal 1: Accuracy drop from baseline ──
        // Measures how much the student's recent accuracy has declined.
        // Normalized by baseline so a drop from 0.9 to 0.7 is proportionally
        // equivalent to a drop from 0.6 to 0.4.
        double accuracyDrop;
        if (baselineAccuracy > MinDenominator)
        {
            accuracyDrop = Math.Max(0.0, (baselineAccuracy - rollingAccuracy5) / baselineAccuracy);
        }
        else
        {
            // No baseline data: assume no drop
            accuracyDrop = 0.0;
        }
        accuracyDrop = Math.Clamp(accuracyDrop, 0.0, 1.0);

        // ── Signal 2: Response time increase from baseline ──
        // Measures how much slower the student is responding compared to baseline.
        // Slower responses indicate cognitive fatigue or increased difficulty.
        double rtIncrease;
        if (baselineRt > MinDenominator)
        {
            rtIncrease = Math.Max(0.0, (rollingRt5 - baselineRt) / baselineRt);
        }
        else
        {
            // No baseline: assume no increase
            rtIncrease = 0.0;
        }
        rtIncrease = Math.Clamp(rtIncrease, 0.0, 1.0);

        // ── Signal 3: Session time fraction ──
        // Measures how far into the session the student is. Fatigue naturally
        // increases as session duration grows.
        double timeFraction;
        if (maxSessionMin > MinDenominator)
        {
            timeFraction = elapsedMin / maxSessionMin;
        }
        else
        {
            timeFraction = 0.0;
        }
        timeFraction = Math.Clamp(timeFraction, 0.0, 1.0);

        // ── Composite fatigue score ──
        double fatigueScore = W1_AccuracyDrop * accuracyDrop
                            + W2_RtIncrease * rtIncrease
                            + W3_TimeFraction * timeFraction;

        // Clamp to [0, 1] (should already be in range due to component clamping,
        // but guard against floating-point edge cases)
        fatigueScore = Math.Clamp(fatigueScore, 0.0, 1.0);

        // Classify fatigue level
        string level = fatigueScore switch
        {
            < 0.3 => "low",
            < 0.6 => "moderate",
            < 0.8 => "high",
            _     => "critical"
        };

        return new FatigueAssessment(
            FatigueScore: fatigueScore,
            AccuracyDropComponent: W1_AccuracyDrop * accuracyDrop,
            RtIncreaseComponent: W2_RtIncrease * rtIncrease,
            TimeFractionComponent: W3_TimeFraction * timeFraction,
            IsHighFatigue: fatigueScore >= HighFatigueThreshold,
            Level: level);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ComputeCooldownMinutes(double fatigueScore)
    {
        // Clamp input to valid range
        double clamped = Math.Clamp(fatigueScore, 0.0, 1.0);

        // Linear interpolation: [0, 1] -> [MinCooldown, MaxCooldown]
        // At fatigue=0.0: 5 minutes (minimum break)
        // At fatigue=1.0: 30 minutes (maximum recovery period)
        double raw = MinCooldownMinutes + clamped * (MaxCooldownMinutes - MinCooldownMinutes);

        // Round to nearest integer, clamp to valid range
        int cooldown = (int)Math.Round(raw);
        if (cooldown < MinCooldownMinutes) cooldown = MinCooldownMinutes;
        if (cooldown > MaxCooldownMinutes) cooldown = MaxCooldownMinutes;

        return cooldown;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DifficultyAdjustment RecommendDifficultyAdjustment(double fatigueScore, int currentDifficulty)
    {
        double clamped = Math.Clamp(fatigueScore, 0.0, 1.0);

        // High fatigue: reduce difficulty to prevent cognitive overload.
        // When fatigue >= 0.7, the student is showing clear signs of struggle
        // and needs easier material to maintain engagement and prevent burnout.
        if (clamped >= EaseThreshold)
        {
            return DifficultyAdjustment.Ease;
        }

        // Low fatigue with room to grow: increase difficulty to maintain flow state.
        // Only increase if the student isn't already at a high difficulty level (>= 8)
        // to prevent premature jumps to expert-level material.
        if (clamped <= IncreaseThreshold && currentDifficulty < MaxDifficultyForIncrease)
        {
            return DifficultyAdjustment.Increase;
        }

        // Moderate fatigue or already at high difficulty: maintain current level.
        return DifficultyAdjustment.Maintain;
    }
}
