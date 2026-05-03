// =============================================================================
// Cena Platform — LinkedStudent (EPIC-PRR-I PRR-293/324, ADR-0057)
//
// Represents a single student linked into a parent's subscription. The
// primary student (1st) has ordinal 0; siblings 1..N. Discount applied per
// TierCatalog.SiblingMonthlyPrice(ordinal).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Linked-student slot on a subscription. Primary has
/// <see cref="Ordinal"/> = 0 and pays the <see cref="TierDefinition.MonthlyPrice"/>;
/// siblings have ordinals 1..N and pay the sibling rate per ordinal.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">
/// Wire-format encrypted student id (ADR-0038). Never stored plaintext.
/// </param>
/// <param name="Ordinal">0 = primary, 1..N = sibling ordinal.</param>
/// <param name="Tier">Tier this student is entitled to on this subscription.</param>
/// <param name="LinkedAt">When this student joined the subscription.</param>
public sealed record LinkedStudent(
    string StudentSubjectIdEncrypted,
    int Ordinal,
    SubscriptionTier Tier,
    DateTimeOffset LinkedAt);
