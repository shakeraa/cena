// =============================================================================
// Cena Platform -- Feature Flag Events (FIND-arch-024)
// Event sourcing for feature flag changes with audit trail.
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Event emitted when a feature flag is created or updated.
/// FIND-arch-024: Provides audit trail and persistence for feature flags.
/// </summary>
public sealed record FeatureFlagSet_V1(
    string FlagName,
    bool Enabled,
    double RolloutPercent,
    string SetByUserId,
    string? Reason,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

/// <summary>
/// Event emitted when a feature flag is deleted.
/// </summary>
public sealed record FeatureFlagDeleted_V1(
    string FlagName,
    string DeletedByUserId,
    DateTimeOffset Timestamp
) : IDelegatedEvent;
