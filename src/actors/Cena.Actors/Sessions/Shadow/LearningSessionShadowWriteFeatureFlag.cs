// =============================================================================
// Cena Platform — LearningSession shadow-write feature flag
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Simple env-var-based feature flag. The repo has a full event-sourced
// FeatureFlagActor, but that is overkill for a one-line rollout switch
// that must be evaluated on every SessionStarted — the path is hot, the
// semantics are boolean, and the rollback case is "flip and redeploy".
//
// Env var: CENA_LEARNING_SESSION_SHADOW_WRITE
// Accepted truthy values (case-insensitive): "1", "true", "yes", "on".
// Anything else (including unset) is false.
//
// This matches the env-var convention already used by other infra flags
// (CENA_CAS_GATE_MODE, CENA_MIGRATIONS_DIR, PILOT_EXPORT_SALT).
// =============================================================================

namespace Cena.Actors.Sessions.Shadow;

/// <summary>
/// Static feature-flag lookup for the LearningSession shadow-write path.
/// </summary>
public static class LearningSessionShadowWriteFeatureFlag
{
    /// <summary>The env-var name that controls the flag.</summary>
    public const string EnvVarName = "CENA_LEARNING_SESSION_SHADOW_WRITE";

    /// <summary>
    /// True iff the <see cref="EnvVarName"/> environment variable is set to
    /// a truthy value ("1", "true", "yes", "on", case-insensitive). The
    /// lookup is cheap; callers may invoke on every write without concern.
    /// </summary>
    public static bool IsEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVarName);
        return IsTruthy(raw);
    }

    /// <summary>
    /// Overload for test doubles that want to inspect a specific string
    /// without mutating process-wide environment state.
    /// </summary>
    public static bool IsTruthy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false
        };
    }
}
