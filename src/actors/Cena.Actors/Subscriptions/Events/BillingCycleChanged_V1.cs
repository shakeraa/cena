// =============================================================================
// Cena Platform — BillingCycleChanged_V1 (EPIC-PRR-I PRR-292)
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Billing cycle transitioned (monthly ↔ annual). Monthly → Annual applies
/// at next cycle end; Annual → Monthly at next renewal boundary.
/// </summary>
public sealed record BillingCycleChanged_V1(
    string ParentSubjectIdEncrypted,
    BillingCycle FromCycle,
    BillingCycle ToCycle,
    DateTimeOffset ChangedAt,
    DateTimeOffset EffectiveAt);
