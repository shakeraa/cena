// =============================================================================
// Cena Platform -- Actor Supervision Strategies
// =============================================================================

using Proto;

namespace Cena.Actors.Infrastructure;

/// <summary>
/// Central registry of supervision strategies for the Cena actor system.
/// OneForOne strategy: a single child's failure does not affect siblings.
/// </summary>
public static class CenaSupervisionStrategies
{
    /// <summary>
    /// Supervision strategy for StudentActor children (LearningSession,
    /// StagnationDetector, OutreachScheduler).
    /// Restarts child on failure. Stops child after 3 consecutive failures within 60s.
    /// </summary>
    public static ISupervisorStrategy StudentChildStrategy()
    {
        return new OneForOneStrategy(
            (pid, reason) =>
            {
                // Always restart children on failure -- they are stateless
                // (state lives in the parent StudentActor)
                return SupervisorDirective.Restart;
            },
            maxNrOfRetries: 3,
            withinTimeSpan: TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Root-level supervision strategy with exponential backoff for virtual actors.
    /// </summary>
    public static ISupervisorStrategy RootStrategy()
    {
        return new OneForOneStrategy(
            (pid, reason) => SupervisorDirective.Restart,
            maxNrOfRetries: 5,
            withinTimeSpan: TimeSpan.FromMinutes(2));
    }
}
