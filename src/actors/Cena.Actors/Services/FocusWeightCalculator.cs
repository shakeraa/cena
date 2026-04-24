// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus Weight Calculator (FOC-001.2)
//
// Adaptive weighting system for the focus signal pipeline.
// When no sensor data is available, returns the original 4-signal weights.
// When sensor data is available, redistributes weights across up to 8 signals.
// Partial sensor availability interpolates proportionally.
//
// Weight invariant: all weights always sum to 1.0 (±0.001).
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Bitmask indicating which mobile sensor signals are available.
/// </summary>
[Flags]
public enum SensorAvailability
{
    None = 0,
    Motion = 1 << 0,       // Accelerometer/gyroscope
    AppFocus = 1 << 1,     // App lifecycle events
    TouchPattern = 1 << 2, // Tap rhythm, swipe velocity
    Environment = 1 << 3,  // Ambient light, proximity
    All = Motion | AppFocus | TouchPattern | Environment
}

/// <summary>
/// Holds weights for all focus signals. Sensor signal weights are zero when unavailable.
/// </summary>
public readonly record struct FocusWeights(
    double Attention,
    double Engagement,
    double Trend,
    double Vigilance,
    double Motion,
    double AppFocus,
    double TouchPattern,
    double Environment
)
{
    public double Sum() =>
        Attention + Engagement + Trend + Vigilance +
        Motion + AppFocus + TouchPattern + Environment;

    public int ActiveSignalCount =>
        (Attention > 0 ? 1 : 0) + (Engagement > 0 ? 1 : 0) +
        (Trend > 0 ? 1 : 0) + (Vigilance > 0 ? 1 : 0) +
        (Motion > 0 ? 1 : 0) + (AppFocus > 0 ? 1 : 0) +
        (TouchPattern > 0 ? 1 : 0) + (Environment > 0 ? 1 : 0);
}

/// <summary>
/// Pure function: computes focus signal weights based on sensor availability.
/// </summary>
public static class FocusWeightCalculator
{
    // ── Original 4-signal weights (no sensors) ──
    private const double W4_Attention = 0.30;
    private const double W4_Engagement = 0.20;
    private const double W4_Trend = 0.25;
    private const double W4_Vigilance = 0.25;

    // ── Full 8-signal weights (all sensors available) ──
    private const double W8_Attention = 0.20;
    private const double W8_Engagement = 0.12;
    private const double W8_Trend = 0.15;
    private const double W8_Vigilance = 0.15;
    private const double W8_Motion = 0.12;
    private const double W8_AppFocus = 0.10;
    private const double W8_TouchPattern = 0.08;
    private const double W8_Environment = 0.08;

    /// <summary>
    /// Compute signal weights based on which sensors are available.
    ///
    /// Algorithm:
    /// 1. Start with the full 8-signal weight set.
    /// 2. Zero out unavailable sensor signals.
    /// 3. Redistribute their weight proportionally across remaining signals.
    ///
    /// When NO sensors: result matches original [0.30, 0.20, 0.25, 0.25, 0, 0, 0, 0].
    /// When ALL sensors: result is the full 8-signal set.
    /// When PARTIAL: proportional redistribution ensures sum = 1.0.
    /// </summary>
    public static FocusWeights ComputeWeights(SensorAvailability availability)
    {
        if (availability == SensorAvailability.None)
        {
            return new FocusWeights(
                Attention: W4_Attention,
                Engagement: W4_Engagement,
                Trend: W4_Trend,
                Vigilance: W4_Vigilance,
                Motion: 0, AppFocus: 0, TouchPattern: 0, Environment: 0
            );
        }

        if (availability == SensorAvailability.All)
        {
            return new FocusWeights(
                Attention: W8_Attention,
                Engagement: W8_Engagement,
                Trend: W8_Trend,
                Vigilance: W8_Vigilance,
                Motion: W8_Motion,
                AppFocus: W8_AppFocus,
                TouchPattern: W8_TouchPattern,
                Environment: W8_Environment
            );
        }

        // ── Partial sensor availability: proportional redistribution ──
        // Start with 8-signal weights, zero out missing sensors, redistribute.
        double motionW = availability.HasFlag(SensorAvailability.Motion) ? W8_Motion : 0;
        double appFocusW = availability.HasFlag(SensorAvailability.AppFocus) ? W8_AppFocus : 0;
        double touchW = availability.HasFlag(SensorAvailability.TouchPattern) ? W8_TouchPattern : 0;
        double envW = availability.HasFlag(SensorAvailability.Environment) ? W8_Environment : 0;

        // Weight from unavailable sensors that needs redistribution
        double unavailableWeight =
            (availability.HasFlag(SensorAvailability.Motion) ? 0 : W8_Motion) +
            (availability.HasFlag(SensorAvailability.AppFocus) ? 0 : W8_AppFocus) +
            (availability.HasFlag(SensorAvailability.TouchPattern) ? 0 : W8_TouchPattern) +
            (availability.HasFlag(SensorAvailability.Environment) ? 0 : W8_Environment);

        // Sum of weights for signals that ARE active (core 4 + available sensors)
        double activeWeight = W8_Attention + W8_Engagement + W8_Trend + W8_Vigilance +
                              motionW + appFocusW + touchW + envW;

        // Scale factor to redistribute unavailable weight proportionally
        double scale = (activeWeight + unavailableWeight) / activeWeight;

        return new FocusWeights(
            Attention: W8_Attention * scale,
            Engagement: W8_Engagement * scale,
            Trend: W8_Trend * scale,
            Vigilance: W8_Vigilance * scale,
            Motion: motionW * scale,
            AppFocus: appFocusW * scale,
            TouchPattern: touchW * scale,
            Environment: envW * scale
        );
    }

    /// <summary>
    /// Build SensorAvailability from a FocusInput's nullable sensor fields.
    /// </summary>
    public static SensorAvailability FromInput(FocusInput input)
    {
        var flags = SensorAvailability.None;
        if (input.MotionStabilityScore.HasValue) flags |= SensorAvailability.Motion;
        if (input.AppFocusScore.HasValue) flags |= SensorAvailability.AppFocus;
        if (input.TouchPatternScore.HasValue) flags |= SensorAvailability.TouchPattern;
        if (input.EnvironmentScore.HasValue) flags |= SensorAvailability.Environment;
        return flags;
    }
}
