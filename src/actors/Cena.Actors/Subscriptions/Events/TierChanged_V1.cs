// =============================================================================
// Cena Platform — TierChanged_V1 (EPIC-PRR-I PRR-310/311)
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Tier upgrade or downgrade. Upgrades take effect immediately; downgrades
/// apply at next renewal (rule enforced in SubscriptionCommands).
/// </summary>
public sealed record TierChanged_V1(
    string ParentSubjectIdEncrypted,
    SubscriptionTier FromTier,
    SubscriptionTier ToTier,
    DateTimeOffset ChangedAt,
    DateTimeOffset EffectiveAt);
