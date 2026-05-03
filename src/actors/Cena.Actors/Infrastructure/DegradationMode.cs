// =============================================================================
// Cena Platform -- Graceful Degradation (RES-006)
// Shared degradation tier logic consumed by actors under health pressure.
// Origin: Fortnite was all-or-nothing — no middle ground between 100% and dead.
// =============================================================================

namespace Cena.Actors.Infrastructure;

/// <summary>
/// Maps SystemHealthLevel to concrete degradation behaviors.
/// Actors query this to decide what features to disable at each tier.
/// </summary>
public static class DegradationMode
{
    /// <summary>Tier 1+: LLM-generated content unavailable, use pre-built pools.</summary>
    public static bool ShouldUseFallbackQuestions(SystemHealthLevel level) =>
        level >= SystemHealthLevel.Degraded;

    /// <summary>Tier 2+: Skip Marten writes, buffer events in memory.</summary>
    public static bool ShouldBufferEvents(SystemHealthLevel level) =>
        level >= SystemHealthLevel.Critical;

    /// <summary>Tier 3: Reject new sessions entirely.</summary>
    public static bool ShouldRejectNewSessions(SystemHealthLevel level) =>
        level >= SystemHealthLevel.Emergency;

    /// <summary>Tier 2+: Serve state from Redis cache instead of event replay.</summary>
    public static bool ShouldServeCachedState(SystemHealthLevel level) =>
        level >= SystemHealthLevel.Critical;

    /// <summary>Tier 3: Aggressively passivate idle actors.</summary>
    public static bool ShouldAggressivelyPassivate(SystemHealthLevel level) =>
        level >= SystemHealthLevel.Emergency;
}
