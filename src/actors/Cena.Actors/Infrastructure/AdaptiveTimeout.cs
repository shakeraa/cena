// =============================================================================
// Cena Platform -- Adaptive Timeouts Under Load (RES-009)
// Origin: Fortnite April 2018 — 200ms static timeout caused connection storms
// that amplified failure. Under load, slightly slower > rapid failure churn.
// =============================================================================

namespace Cena.Actors.Infrastructure;

/// <summary>
/// Calculates adaptive timeouts based on system health level.
/// Under load, timeouts are extended to prevent rapid disconnect/reconnect churn.
/// </summary>
public static class AdaptiveTimeout
{
    /// <summary>
    /// Returns an adjusted timeout: base * multiplier based on health level.
    /// Healthy=1x, Degraded=1.5x, Critical=2x, Emergency=3x.
    /// </summary>
    public static TimeSpan Calculate(TimeSpan baseTimeout, SystemHealthLevel health)
    {
        var multiplier = health switch
        {
            SystemHealthLevel.Healthy   => 1.0,
            SystemHealthLevel.Degraded  => 1.5,
            SystemHealthLevel.Critical  => 2.0,
            SystemHealthLevel.Emergency => 3.0,
            _ => 1.0
        };
        return TimeSpan.FromMilliseconds(baseTimeout.TotalMilliseconds * multiplier);
    }
}
